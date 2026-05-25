using System.Text.Json;
using Ape.Worker.Sdk.Messaging;

namespace Ape.AutomationRunner.Workflows.TaskHandlers;

public interface IWorkflowRunRepository
{
    Task<IReadOnlyList<WorkflowEventCandidate>> GetWaitingStepsExpectingEventAsync(
        string tenantKey,
        string messageType,
        CancellationToken cancellationToken
    );

    Task MarkStepWaitingAsync(
        long workflowRunId,
        string stepKey,
        string commandMessageId,
        string expectedCompletedMessageType,
        string expectedFailedMessageType,
        DateTimeOffset timeoutAtUtc,
        CancellationToken cancellationToken
    );
}

public sealed class NullWorkflowRunRepository : IWorkflowRunRepository
{
    public Task<IReadOnlyList<WorkflowEventCandidate>> GetWaitingStepsExpectingEventAsync(
        string tenantKey,
        string messageType,
        CancellationToken cancellationToken
    ) => Task.FromResult<IReadOnlyList<WorkflowEventCandidate>>(Array.Empty<WorkflowEventCandidate>());

    public Task MarkStepWaitingAsync(
        long workflowRunId,
        string stepKey,
        string commandMessageId,
        string expectedCompletedMessageType,
        string expectedFailedMessageType,
        DateTimeOffset timeoutAtUtc,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;
}

public sealed class ModuleRequestTaskHandler(
    IMessagePublisher publisher,
    WorkflowPayloadTemplateRenderer renderer,
    IWorkflowRunRepository workflowRunRepository
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

        MessageEnvelope envelope = new(
            commandMessageId,
            runContext.CorrelationId,
            causeEnvelope.MessageId,
            runContext.TenantKey,
            "Ape.AutomationRunner",
            messageType,
            1,
            DateTimeOffset.UtcNow,
            metadata,
            rendered
        );

        await publisher.PublishAsync(envelope, messageType, cancellationToken);

        DateTimeOffset timeoutAtUtc = DateTimeOffset.UtcNow.AddSeconds(step.TimeoutSeconds ?? 300);
        await workflowRunRepository.MarkStepWaitingAsync(
            runContext.WorkflowRunId,
            step.StepKey,
            commandMessageId,
            expectedCompleted,
            expectedFailed,
            timeoutAtUtc,
            cancellationToken
        );

        return WorkflowStepRuntimeStatus.Waiting;
    }
}
