using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ape.AutomationRunner.Workflows;

public sealed class WorkflowPayloadTemplateRenderer
{
    private static readonly Regex PlaceholderRegex = new("{{\\s*(.*?)\\s*}}", RegexOptions.Compiled);

    public JsonElement Render(
        JsonElement payload,
        JsonElement triggerPayload,
        IReadOnlyDictionary<string, JsonElement> stepOutputs,
        string correlationId = ""
    )
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            WriteElement(writer, payload, triggerPayload, stepOutputs, correlationId);
        }

        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private void WriteElement(
        Utf8JsonWriter writer,
        JsonElement element,
        JsonElement triggerPayload,
        IReadOnlyDictionary<string, JsonElement> stepOutputs,
        string correlationId
    )
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty p in element.EnumerateObject())
            {
                writer.WritePropertyName(p.Name);
                WriteElement(writer, p.Value, triggerPayload, stepOutputs, correlationId);
            }

            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement i in element.EnumerateArray())
            {
                WriteElement(writer, i, triggerPayload, stepOutputs, correlationId);
            }

            writer.WriteEndArray();
            return;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            string value = element.GetString() ?? string.Empty;
            MatchCollection matches = PlaceholderRegex.Matches(value);
            if (matches.Count == 1 && matches[0].Value.Length == value.Length)
            {
                JsonElement resolved = ResolvePlaceholderElement(
                    matches[0].Groups[1].Value,
                    triggerPayload,
                    stepOutputs,
                    correlationId
                );
                resolved.WriteTo(writer);
                return;
            }

            writer.WriteStringValue(PlaceholderRegex.Replace(value, m =>
                ElementToTemplateString(
                    ResolvePlaceholderElement(
                        m.Groups[1].Value,
                        triggerPayload,
                        stepOutputs,
                        correlationId
                    )
                )
            ));
            return;
        }

        element.WriteTo(writer);
    }

    private JsonElement ResolvePlaceholderElement(
        string expression,
        JsonElement triggerPayload,
        IReadOnlyDictionary<string, JsonElement> stepOutputs,
        string correlationId
    )
    {
        string[] parts = expression.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 1 && parts[0] == "correlationId")
        {
            return JsonSerializer.SerializeToElement(correlationId);
        }

        if (parts.Length >= 3 && parts[0] == "trigger" && parts[1] == "payload")
        {
            return ResolveJson(triggerPayload, parts.Skip(2).ToArray(), expression);
        }

        if (parts.Length >= 4
            && parts[0] == "steps"
            && parts[2] == "outputs"
            && stepOutputs.TryGetValue(parts[1], out JsonElement output))
        {
            return ResolveJson(output, parts.Skip(3).ToArray(), expression);
        }

        throw new InvalidOperationException($"Unsupported template expression: {expression}");
    }

    private static JsonElement ResolveJson(JsonElement element, string[] path, string expression)
    {
        JsonElement current = element;
        foreach (string segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out current))
            {
                throw new InvalidOperationException(
                    $"Missing value for template expression: {expression}"
                );
            }
        }

        return current;
    }

    private static string ElementToTemplateString(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        return element.GetRawText();
    }
}
