using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ape.AutomationRunner.Workflows;

public sealed class WorkflowPayloadTemplateRenderer
{
    private static readonly Regex PlaceholderRegex = new("{{\\s*(.*?)\\s*}}", RegexOptions.Compiled);

    public JsonElement Render(
        JsonElement payload,
        JsonElement workflowInputs,
        IReadOnlyDictionary<string, JsonElement> stepOutputs
    )
    {
        object? converted = ConvertElement(payload, workflowInputs, stepOutputs);
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(converted));
    }

    private object? ConvertElement(
        JsonElement element,
        JsonElement workflowInputs,
        IReadOnlyDictionary<string, JsonElement> stepOutputs
    )
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            Dictionary<string, object?> o = new();
            foreach (JsonProperty p in element.EnumerateObject())
            {
                o[p.Name] = ConvertElement(p.Value, workflowInputs, stepOutputs);
            }

            return o;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            List<object?> a = new();
            foreach (JsonElement i in element.EnumerateArray())
            {
                a.Add(ConvertElement(i, workflowInputs, stepOutputs));
            }

            return a;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            string value = element.GetString() ?? string.Empty;
            return PlaceholderRegex.Replace(
                value,
                m => ResolvePlaceholder(m.Groups[1].Value, workflowInputs, stepOutputs)
            );
        }

        return JsonSerializer.Deserialize<object>(element.GetRawText());
    }

    private string ResolvePlaceholder(
        string expression,
        JsonElement workflowInputs,
        IReadOnlyDictionary<string, JsonElement> stepOutputs
    )
    {
        string[] parts = expression.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 3 && parts[0] == "workflow" && parts[1] == "inputs")
        {
            return ResolveJson(workflowInputs, parts.Skip(2).ToArray());
        }

        if (parts.Length >= 4
            && parts[0] == "steps"
            && parts[2] == "outputs"
            && stepOutputs.TryGetValue(parts[1], out JsonElement output))
        {
            return ResolveJson(output, parts.Skip(3).ToArray());
        }

        throw new InvalidOperationException($"Unknown placeholder: {expression}");
    }

    private static string ResolveJson(JsonElement element, string[] path)
    {
        JsonElement current = element;
        foreach (string segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out current))
            {
                throw new InvalidOperationException(
                    $"Cannot resolve placeholder path: {string.Join('.', path)}"
                );
            }
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString() ?? string.Empty
            : current.ToString();
    }
}
