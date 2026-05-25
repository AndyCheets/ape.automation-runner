using Ape.Worker.Sdk.Messaging;
using Microsoft.Extensions.Logging;

namespace Ape.AutomationRunner.Messaging;

public sealed class WorkflowProgressEventHandler(ILogger<WorkflowProgressEventHandler> logger) : IMessageHandler
{
    public string MessageType => "*";

    public Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        logger.LogDebug("Workflow event candidate {MessageType}", envelope.MessageType);
        return Task.CompletedTask;
    }
}
