using Ape.Worker.Sdk.Messaging;
using Microsoft.Extensions.Logging;

namespace Ape.AutomationRunner.Messaging;

public sealed class RunWorkflowCommandHandler(ILogger<RunWorkflowCommandHandler> logger) : IMessageHandler
{
    public string MessageType => "RunWorkflow";

    public Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "RunWorkflow received for {TenantKey} correlation {CorrelationId}",
            envelope.TenantKey,
            envelope.CorrelationId
        );
        return Task.CompletedTask;
    }
}
