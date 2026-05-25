using System.Text.Json;
using YamlDotNet.Serialization;
namespace Ape.AutomationRunner.Workflows;
public sealed class WorkflowDefinitionParser
{
    public WorkflowDefinition Parse(string yaml)
    {
        IDeserializer deserializer = new DeserializerBuilder().Build();
        Dictionary<object, object> root = deserializer.Deserialize<Dictionary<object, object>>(yaml);
        string workflowKey = root.GetValueOrDefault("workflowKey")?.ToString() ?? string.Empty;
        int version = int.TryParse(root.GetValueOrDefault("version")?.ToString(), out int parsed) ? parsed : 0;
        string name = root.GetValueOrDefault("name")?.ToString() ?? string.Empty;
        List<WorkflowStepDefinition> steps = new();
        if (root.TryGetValue("steps", out object? stepsObject) && stepsObject is List<object> stepList)
        {
            foreach (object stepObj in stepList)
            {
                Dictionary<object, object> step = (Dictionary<object, object>)stepObj;
                object? configObj = step.GetValueOrDefault("config") ?? new Dictionary<object, object>();
                string configJson = JsonSerializer.Serialize(configObj);
                JsonElement config = JsonSerializer.Deserialize<JsonElement>(configJson);
                int? timeoutSeconds = int.TryParse(step.GetValueOrDefault("timeoutSeconds")?.ToString(), out int timeout) ? timeout : null;
                steps.Add(new WorkflowStepDefinition(step.GetValueOrDefault("stepKey")?.ToString() ?? string.Empty, step.GetValueOrDefault("taskType")?.ToString() ?? string.Empty, timeoutSeconds, config));
            }
        }
        return new WorkflowDefinition(workflowKey, version, name, steps);
    }
}
