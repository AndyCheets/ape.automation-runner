using System.Text.Json;
namespace Ape.Worker.Sdk.Messaging;
public sealed class MessageEnvelopeSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public MessageEnvelope Deserialize(ReadOnlyMemory<byte> body)
    {
        MessageEnvelope? envelope = JsonSerializer.Deserialize<MessageEnvelope>(body.Span, Options);
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.MessageId) || string.IsNullOrWhiteSpace(envelope.CorrelationId) || string.IsNullOrWhiteSpace(envelope.TenantKey) || string.IsNullOrWhiteSpace(envelope.Source) || string.IsNullOrWhiteSpace(envelope.MessageType) || envelope.SchemaVersion <= 0 || envelope.CreatedAtUtc == default)
        { throw new MessageHandlingException("Invalid APE message envelope."); }
        return envelope;
    }
    public byte[] Serialize(MessageEnvelope envelope) => JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
}
