using System.Text.Json;
using Ape.Worker.Sdk.Messaging;
namespace Ape.AutomationRunner.Workflows.TaskHandlers;
public interface IWorkflowRunRepository { Task MarkStepWaitingAsync(long workflowRunId, string stepKey, string commandMessageId, string expectedCompletedMessageType, string expectedFailedMessageType, DateTimeOffset timeoutAtUtc, CancellationToken cancellationToken); }
public sealed class ModuleRequestTaskHandler(IMessagePublisher publisher, WorkflowPayloadTemplateRenderer renderer, IWorkflowRunRepository workflowRunRepository) : IWorkflowTaskHandler
{
    public string TaskType => "module.request";
    public async Task<WorkflowStepRuntimeStatus> HandleAsync(WorkflowRunContext runContext, WorkflowStepDefinition step, MessageEnvelope causeEnvelope, IReadOnlyDictionary<string, JsonElement> stepOutputs, CancellationToken cancellationToken)
    {
        JsonElement payloadTemplate = step.Config.GetProperty("payload");
        JsonElement rendered = renderer.Render(payloadTemplate, runContext.Inputs, stepOutputs);
        string messageType = step.Config.GetProperty("commandMessageType").GetString() ?? throw new InvalidOperationException("commandMessageType missing");
        string expectedCompleted = step.Config.GetProperty("expectedCompletedMessageType").GetString() ?? throw new InvalidOperationException("expectedCompletedMessageType missing");
        string expectedFailed = step.Config.GetProperty("expectedFailedMessageType").GetString() ?? throw new InvalidOperationException("expectedFailedMessageType missing");
        string commandMessageId = Guid.NewGuid().ToString("N");
        Dictionary<string,string> metadata = new() { ["workflowKey"] = runContext.WorkflowKey, ["workflowVersion"] = runContext.WorkflowVersion.ToString(), ["workflowRunId"] = runContext.WorkflowRunId.ToString(), ["workflowStepKey"] = step.StepKey };
        MessageEnvelope envelope = new(commandMessageId, runContext.CorrelationId, causeEnvelope.MessageId, runContext.TenantKey, "Ape.AutomationRunner", messageType, 1, DateTimeOffset.UtcNow, metadata, rendered);
        await publisher.PublishAsync(envelope, messageType, cancellationToken);
        DateTimeOffset timeoutAtUtc = DateTimeOffset.UtcNow.AddSeconds(step.TimeoutSeconds ?? 300);
        await workflowRunRepository.MarkStepWaitingAsync(runContext.WorkflowRunId, step.StepKey, commandMessageId, expectedCompleted, expectedFailed, timeoutAtUtc, cancellationToken);
        return WorkflowStepRuntimeStatus.Waiting;
    }
}
