using System.Text.Json;
namespace Ape.Worker.Sdk.Messaging;
public sealed record MessageEnvelope(string MessageId,string CorrelationId,string? CausationId,string TenantKey,string Source,string MessageType,int SchemaVersion,DateTimeOffset CreatedAtUtc,IReadOnlyDictionary<string,string> Metadata,JsonElement Payload);
