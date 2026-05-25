namespace Ape.Worker.Sdk.Messaging;
public interface IMessageHandlerRegistry { IMessageHandler Resolve(string messageType); }
