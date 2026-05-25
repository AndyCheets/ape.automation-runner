namespace Ape.Worker.Sdk.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync(MessageEnvelope envelope, string routingKey, CancellationToken cancellationToken);
}
