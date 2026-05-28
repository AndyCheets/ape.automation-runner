using Ape.Worker.Sdk.Messaging;
using Ape.AutomationRunner.Workflows;
using Microsoft.Extensions.Logging;

namespace Ape.AutomationRunner.Messaging;

public sealed class WorkflowProgressEventHandler(
    IWorkflowExecutionEngine workflowExecutionEngine,
    ILogger<WorkflowProgressEventHandler> logger
) : IMessageHandler
{
    public string MessageType => "*";

    public async Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Workflow result event received for tenant {TenantKey} correlation {CorrelationId} event {EventMessageType}",
            envelope.TenantKey,
            envelope.CorrelationId,
            envelope.MessageType
        );

        await workflowExecutionEngine.HandleResultEventAsync(envelope, cancellationToken);
    }
}
