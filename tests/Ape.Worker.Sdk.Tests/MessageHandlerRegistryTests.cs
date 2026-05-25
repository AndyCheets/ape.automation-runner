using Ape.Worker.Sdk.Messaging;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Ape.Worker.Sdk.Tests;

public sealed class MessageHandlerRegistryTests
{
    [Test]
    public void Resolve_RegisteredHandler_ReturnsHandler()
    {
        ServiceCollection services = new();
        services.AddSingleton<IMessageHandler, FakeHandler>();
        services.AddSingleton<IMessageHandlerRegistry, MessageHandlerRegistry>();
        ServiceProvider provider = services.BuildServiceProvider();
        IMessageHandlerRegistry registry = provider.GetRequiredService<IMessageHandlerRegistry>();
        IMessageHandler handler = registry.Resolve("Fake");
        Assert.That(handler, Is.TypeOf<FakeHandler>());
    }

    private sealed class FakeHandler : IMessageHandler
    {
        public string MessageType => "Fake";
        public Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
