using Ape.Worker.Sdk.Messaging;
using System.Text;
using NUnit.Framework;

namespace Ape.Worker.Sdk.Tests;

public sealed class MessageEnvelopeSerializerTests
{
    [Test]
    public void Deserialize_ValidEnvelope_ReturnsEnvelope()
    {
        MessageEnvelopeSerializer serializer = new();
        byte[] json = Encoding.UTF8.GetBytes("""
        {"messageId":"1","correlationId":"2","causationId":null,"tenantKey":"t","source":"s","messageType":"SampleCommand","schemaVersion":1,"createdAtUtc":"2026-01-01T00:00:00Z","metadata":{},"payload":{}}
        """);
        MessageEnvelope result = serializer.Deserialize(json);
        Assert.That(result.MessageType, Is.EqualTo("SampleCommand"));
    }
}
