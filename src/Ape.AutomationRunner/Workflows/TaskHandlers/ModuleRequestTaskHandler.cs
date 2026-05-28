using System.Text.Json;
using Ape.AutomationRunner.Configuration;
using Ape.Worker.Sdk.Configuration;
using Ape.Worker.Sdk.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ape.AutomationRunner.Workflows.TaskHandlers;

public interface IWorkflowRunRepository
{
    Task<long> CreateWorkflowRunAsync(
        string tenantKey,
        string correlationId,
        string workflowKey,
        int workflowVersion,
        JsonElement inputs,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken
    );

    Task CreateWorkflowStepAsync(
        string tenantKey,
        long workflowRunId,
        string stepKey,
        string taskType,
        WorkflowStepRuntimeStatus status,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<WorkflowEventCandidate>> GetWaitingStepsExpectingEventAsync(
        string tenantKey,
        string messageType,
        CancellationToken cancellationToken
    );

    Task MarkStepWaitingAsync(
        string tenantKey,
        long workflowRunId,
        string stepKey,
        string commandMessageId,
        string expectedCompletedMessageType,
        string expectedFailedMessageType,
        DateTimeOffset timeoutAtUtc,
        CancellationToken cancellationToken
    );

    Task MarkStepFailedAsync(
        string tenantKey,
        long workflowRunId,
        string stepKey,
        string failureReason,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken
    );

    Task MarkWorkflowFailedAsync(
        string tenantKey,
        long workflowRunId,
        string failureReason,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken
    );
}

public sealed class NullWorkflowRunRepository : IWorkflowRunRepository
{
    public Task<long> CreateWorkflowRunAsync(
        string tenantKey,
        string correlationId,
        string workflowKey,
        int workflowVersion,
        JsonElement inputs,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken
    ) => Task.FromResult(0L);

    public Task CreateWorkflowStepAsync(
        string tenantKey,
        long workflowRunId,
        string stepKey,
        string taskType,
        WorkflowStepRuntimeStatus status,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    public Task<IReadOnlyList<WorkflowEventCandidate>> GetWaitingStepsExpectingEventAsync(
        string tenantKey,
        string messageType,
        CancellationToken cancellationToken
    ) => Task.FromResult<IReadOnlyList<WorkflowEventCandidate>>(Array.Empty<WorkflowEventCandidate>());

    public Task MarkStepWaitingAsync(
        string tenantKey,
        long workflowRunId,
        string stepKey,
        string commandMessageId,
        string expectedCompletedMessageType,
        string expectedFailedMessageType,
        DateTimeOffset timeoutAtUtc,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    public Task MarkStepFailedAsync(
        string tenantKey,
        long workflowRunId,
        string stepKey,
        string failureReason,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    public Task MarkWorkflowFailedAsync(
        string tenantKey,
        long workflowRunId,
        string failureReason,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;
}

public sealed class ModuleRequestTaskHandler(
    IMessagePublisher publisher,
    WorkflowPayloadTemplateRenderer renderer,
    IWorkflowRunRepository workflowRunRepository,
    IOptions<WorkflowRunnerOptions> workflowRunnerOptions,
    IOptions<ServiceIdentityOptions> serviceIdentityOptions,
    ILogger<ModuleRequestTaskHandler> logger
) : IWorkflowTaskHandler
{
    public string TaskType => "module.request";

    public async Task<WorkflowStepRuntimeStatus> HandleAsync(
        WorkflowRunContext runContext,
        WorkflowStepDefinition step,
        MessageEnvelope causeEnvelope,
        IReadOnlyDictionary<string, JsonElement> stepOutputs,
        CancellationToken cancellationToken
    )
    {
        if (step.Config is not ModuleRequestWorkflowTaskConfig config)
        {
            throw new InvalidOperationException(
                $"Step {step.StepKey} config must be {nameof(ModuleRequestWorkflowTaskConfig)}."
            );
        }

        JsonElement payloadTemplate = config.Payload;
        JsonElement rendered = renderer.Render(payloadTemplate, runContext.Inputs, stepOutputs);

        string messageType = config.CommandMessageType;
        string expectedCompleted = config.ExpectedCompletedMessageType;
        string expectedFailed = config.ExpectedFailedMessageType;

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

        MessageEnvelope envelope = new(
            commandMessageId,
            runContext.CorrelationId,
            causeEnvelope.MessageId,
            runContext.TenantKey,
            source,
            messageType,
            1,
            DateTimeOffset.UtcNow,
            metadata,
            rendered
        );

        await publisher.PublishCommandAsync(envelope, cancellationToken);

        logger.LogInformation(
            "Workflow command published for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId} step {WorkflowStepKey} command {CommandMessageType}",
            runContext.TenantKey,
            runContext.CorrelationId,
            runContext.WorkflowKey,
            runContext.WorkflowVersion,
            runContext.WorkflowRunId,
            step.StepKey,
            messageType
        );

        DateTimeOffset timeoutAtUtc = DateTimeOffset.UtcNow.AddSeconds(
            step.TimeoutSeconds ?? workflowRunnerOptions.Value.DefaultStepTimeoutSeconds
        );
        await workflowRunRepository.MarkStepWaitingAsync(
            runContext.TenantKey,
            runContext.WorkflowRunId,
            step.StepKey,
            commandMessageId,
            expectedCompleted,
            expectedFailed,
            timeoutAtUtc,
            cancellationToken
        );

        logger.LogInformation(
            "Workflow step marked Waiting for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId} step {WorkflowStepKey} command {CommandMessageType}",
            runContext.TenantKey,
            runContext.CorrelationId,
            runContext.WorkflowKey,
            runContext.WorkflowVersion,
            runContext.WorkflowRunId,
            step.StepKey,
            messageType
        );

        return WorkflowStepRuntimeStatus.Waiting;
    }
}
