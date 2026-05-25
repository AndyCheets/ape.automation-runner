namespace Ape.Worker.Sdk.Messaging;

public interface IMessageHandler
{
    string MessageType { get; }
    Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken);
}
