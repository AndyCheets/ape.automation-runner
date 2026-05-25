using Microsoft.Extensions.DependencyInjection;

namespace Ape.Worker.Sdk.Messaging;

public sealed class MessageHandlerRegistry(IServiceProvider services) : IMessageHandlerRegistry
{
    public IMessageHandler Resolve(string messageType)
    {
        IEnumerable<IMessageHandler> handlers = services.GetServices<IMessageHandler>();
        IMessageHandler? handler = handlers.FirstOrDefault(
            h => string.Equals(h.MessageType, messageType, StringComparison.OrdinalIgnoreCase)
        ) ?? handlers.FirstOrDefault(
            h => string.Equals(h.MessageType, "*", StringComparison.Ordinal)
        );
        return handler ?? throw new MessageHandlingException("No handler registered for message type.");
    }
}
