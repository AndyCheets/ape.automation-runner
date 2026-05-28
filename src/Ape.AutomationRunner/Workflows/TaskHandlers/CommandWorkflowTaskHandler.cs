using System.Text.Json;
using Ape.AutomationRunner.Configuration;
using Ape.Worker.Sdk.Configuration;
using Ape.Worker.Sdk.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ape.AutomationRunner.Workflows.TaskHandlers;

public sealed class CommandWorkflowTaskHandler(
    IMessagePublisher publisher,
    WorkflowPayloadTemplateRenderer renderer,
    IWorkflowRunRepository workflowRunRepository,
    MessageContractRegistry messageContractRegistry,
    IOptions<WorkflowRunnerOptions> workflowRunnerOptions,
    IOptions<ServiceIdentityOptions> serviceIdentityOptions,
    ILogger<CommandWorkflowTaskHandler> logger
) : IWorkflowTaskHandler
{
    public string TaskType => "command";

    public async Task<WorkflowStepRuntimeStatus> HandleAsync(
        WorkflowRunContext runContext,
        WorkflowStepDefinition step,
        MessageEnvelope causeEnvelope,
        IReadOnlyDictionary<string, JsonElement> stepOutputs,
        CancellationToken cancellationToken
    )
    {
        if (step.Config is not CommandWorkflowTaskConfig config)
        {
            throw new InvalidOperationException(
                $"Step {step.StepKey} config must be {nameof(CommandWorkflowTaskConfig)}."
            );
        }

        MessageContract contract = messageContractRegistry.Get(config.MessageType);
        JsonElement rendered = renderer.Render(
            config.Payload,
            runContext.Inputs,
            stepOutputs,
            runContext.CorrelationId
        );

        logger.LogInformation(
            "Template payload resolved for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId} step {StepId} command {CommandMessageType}",
            runContext.TenantKey,
            runContext.CorrelationId,
            runContext.WorkflowKey,
            runContext.WorkflowVersion,
            runContext.WorkflowRunId,
            step.StepKey,
            config.MessageType
        );

        string commandMessageId = Guid.NewGuid().ToString("N");
        Dictionary<string, string> metadata = new()
        {
            ["workflowKey"] = runContext.WorkflowKey,
            ["workflowVersion"] = runContext.WorkflowVersion.ToString(),
            ["workflowRunId"] = runContext.WorkflowRunId.ToString(),
            ["workflowStepKey"] = step.StepKey,
        };

        string source = string.IsNullOrWhiteSpace(serviceIdentityOptions.Value.Source)
            ? "ape.automation-runner"
            : serviceIdentityOptions.Value.Source;

        MessageEnvelope commandEnvelope = new(
            commandMessageId,
            runContext.CorrelationId,
            null,
            runContext.TenantKey,
            source,
            config.MessageType,
            1,
            DateTimeOffset.UtcNow,
            metadata,
            rendered
        );

        DateTimeOffset timeoutAtUtc = DateTimeOffset.UtcNow.AddSeconds(
            step.TimeoutSeconds ?? workflowRunnerOptions.Value.DefaultStepTimeoutSeconds
        );

        await workflowRunRepository.MarkStepWaitingAsync(
            runContext.TenantKey,
            runContext.WorkflowRunId,
            step.StepKey,
            commandMessageId,
            config.MessageType,
            contract.SuccessEventMessageType,
            contract.FailureEventMessageType,
            rendered,
            timeoutAtUtc,
            cancellationToken
        );
        await workflowRunRepository.MarkWorkflowWaitingAsync(
            runContext.TenantKey,
            runContext.WorkflowRunId,
            step.StepKey,
            DateTimeOffset.UtcNow,
            cancellationToken
        );

        await publisher.PublishCommandAsync(commandEnvelope, cancellationToken);

        logger.LogInformation(
            "Workflow command published for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId} step {StepId} command {CommandMessageType}",
            runContext.TenantKey,
            runContext.CorrelationId,
            runContext.WorkflowKey,
            runContext.WorkflowVersion,
            runContext.WorkflowRunId,
            step.StepKey,
            config.MessageType
        );

        logger.LogInformation(
            "Workflow step waiting for event for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId} step {StepId} command {CommandMessageType} success {SuccessEventMessageType} failure {FailureEventMessageType}",
            runContext.TenantKey,
            runContext.CorrelationId,
            runContext.WorkflowKey,
            runContext.WorkflowVersion,
            runContext.WorkflowRunId,
            step.StepKey,
            config.MessageType,
            contract.SuccessEventMessageType,
            contract.FailureEventMessageType
        );

        return WorkflowStepRuntimeStatus.WaitingForEvent;
    }
}
