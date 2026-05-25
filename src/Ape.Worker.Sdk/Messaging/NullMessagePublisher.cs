using Microsoft.Extensions.Logging;

namespace Ape.Worker.Sdk.Messaging;

public sealed class NullMessagePublisher(ILogger<NullMessagePublisher> logger) : IMessagePublisher
{
    public Task PublishAsync(MessageEnvelope envelope, string routingKey, CancellationToken cancellationToken)
    {
        logger.LogInformation("Publishing {MessageType} to {RoutingKey}", envelope.MessageType, routingKey);
        return Task.CompletedTask;
    }
}
