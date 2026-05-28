using System.Text.Json;

namespace Ape.AutomationRunner.Workflows;

public sealed record MessageOutputMapping(string OutputName, string PayloadPath);

public sealed record MessageContract(
    string CommandMessageType,
    string SuccessEventMessageType,
    string FailureEventMessageType,
    IReadOnlyList<MessageOutputMapping> OutputMappings
);

public sealed class MessageContractRegistry
{
    private readonly IReadOnlyDictionary<string, MessageContract> _contracts;

    public MessageContractRegistry()
    {
        MessageContract[] contracts =
        [
            new(
                "GenerateTextWithAi",
                "AiTextGenerated",
                "AiTextGenerationFailed",
                [new MessageOutputMapping("generatedText", "payload.generatedText")]
            ),
            new(
                "SendTelegramMessage",
                "TelegramMessageSent",
                "TelegramMessageFailed",
                []
            ),
        ];

        _contracts = contracts.ToDictionary(
            c => c.CommandMessageType,
            StringComparer.Ordinal
        );
    }

    public bool IsRegistered(string commandMessageType)
        => _contracts.ContainsKey(commandMessageType);

    public MessageContract Get(string commandMessageType)
        => _contracts.TryGetValue(commandMessageType, out MessageContract? contract)
            ? contract
            : throw new InvalidOperationException(
                $"No message contract is registered for command message type {commandMessageType}."
            );

    public JsonElement MapOutputs(MessageContract contract, JsonElement eventPayload)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            foreach (MessageOutputMapping mapping in contract.OutputMappings)
            {
                JsonElement value = ResolveMapping(eventPayload, mapping.PayloadPath);
                writer.WritePropertyName(mapping.OutputName);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static JsonElement ResolveMapping(JsonElement payload, string path)
    {
        string[] parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] != "payload")
        {
            throw new InvalidOperationException($"Unsupported output mapping path: {path}");
        }

        JsonElement current = payload;
        foreach (string segment in parts.Skip(1))
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out current))
            {
                throw new InvalidOperationException($"Cannot resolve output mapping path: {path}");
            }
        }

        return current;
    }
}
