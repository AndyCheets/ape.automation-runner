using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace Ape.AutomationRunner.Workflows;

public sealed class WorkflowDefinitionParser
{
    public WorkflowDefinition Parse(string yaml)
    {
        using StringReader reader = new(yaml);
        YamlStream stream = new();
        stream.Load(reader);

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return new WorkflowDefinition(string.Empty, 0, string.Empty, Array.Empty<WorkflowStepDefinition>());
        }

        string workflowKey = GetScalar(root, "workflowKey") ?? string.Empty;
        int version = int.TryParse(GetScalar(root, "version"), out int parsedVersion)
            ? parsedVersion
            : 0;
        string name = GetScalar(root, "name") ?? string.Empty;

        List<WorkflowStepDefinition> steps = new();
        if (TryGetValue(root, "steps", out YamlNode? stepsNode)
            && stepsNode is YamlSequenceNode stepSequence)
        {
            foreach (YamlNode stepNode in stepSequence.Children)
            {
                if (stepNode is not YamlMappingNode step)
                {
                    continue;
                }

                string taskType = GetScalar(step, "taskType") ?? string.Empty;
                int? timeoutSeconds = int.TryParse(GetScalar(step, "timeoutSeconds"), out int timeout)
                    ? timeout
                    : null;

                WorkflowTaskConfig config = ParseTaskConfig(taskType, step);
                steps.Add(
                    new WorkflowStepDefinition(
                        GetScalar(step, "stepKey") ?? string.Empty,
                        taskType,
                        timeoutSeconds,
                        config
                    )
                );
            }
        }

        return new WorkflowDefinition(workflowKey, version, name, steps);
    }

    private static WorkflowTaskConfig ParseTaskConfig(string taskType, YamlMappingNode step)
    {
        YamlMappingNode config = TryGetValue(step, "config", out YamlNode? configNode)
            && configNode is YamlMappingNode configMapping
                ? configMapping
                : new YamlMappingNode();

        if (taskType == "module.request")
        {
            JsonElement payload = TryGetValue(config, "payload", out YamlNode? payloadNode)
                && payloadNode is not null
                ? ToJsonElement(payloadNode)
                : ToJsonElement(new YamlMappingNode());

            return new ModuleRequestWorkflowTaskConfig(
                GetScalar(config, "commandMessageType") ?? string.Empty,
                GetScalar(config, "expectedCompletedMessageType") ?? string.Empty,
                GetScalar(config, "expectedFailedMessageType") ?? string.Empty,
                payload
            );
        }

        return new UnknownWorkflowTaskConfig(taskType, ToJsonElement(config));
    }

    private static string? GetScalar(YamlMappingNode mapping, string key)
        => TryGetValue(mapping, key, out YamlNode? value) && value is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private static bool TryGetValue(YamlMappingNode mapping, string key, out YamlNode? value)
    {
        foreach (KeyValuePair<YamlNode, YamlNode> child in mapping.Children)
        {
            if (child.Key is YamlScalarNode scalar
                && string.Equals(scalar.Value, key, StringComparison.Ordinal))
            {
                value = child.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static JsonElement ToJsonElement(YamlNode node)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            WriteYamlNode(writer, node);
        }

        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteYamlNode(Utf8JsonWriter writer, YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                writer.WriteStartObject();
                foreach (KeyValuePair<YamlNode, YamlNode> child in mapping.Children)
                {
                    if (child.Key is not YamlScalarNode key)
                    {
                        throw new InvalidOperationException("YAML mapping keys must be scalar values.");
                    }

                    writer.WritePropertyName(key.Value ?? string.Empty);
                    WriteYamlNode(writer, child.Value);
                }

                writer.WriteEndObject();
                break;

            case YamlSequenceNode sequence:
                writer.WriteStartArray();
                foreach (YamlNode child in sequence.Children)
                {
                    WriteYamlNode(writer, child);
                }

                writer.WriteEndArray();
                break;

            case YamlScalarNode scalar:
                WriteScalar(writer, scalar.Value);
                break;

            default:
                writer.WriteNullValue();
                break;
        }
    }

    private static void WriteScalar(Utf8JsonWriter writer, string? value)
    {
        if (value is null || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            writer.WriteNullValue();
            return;
        }

        if (bool.TryParse(value, out bool boolValue))
        {
            writer.WriteBooleanValue(boolValue);
            return;
        }

        if (long.TryParse(value, out long longValue))
        {
            writer.WriteNumberValue(longValue);
            return;
        }

        if (double.TryParse(value, out double doubleValue))
        {
            writer.WriteNumberValue(doubleValue);
            return;
        }

        writer.WriteStringValue(value);
    }
}
