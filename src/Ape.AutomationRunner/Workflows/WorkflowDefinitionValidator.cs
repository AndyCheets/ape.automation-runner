namespace Ape.AutomationRunner.Workflows;

public sealed class WorkflowDefinitionValidator
{
    public IReadOnlyList<string> Validate(WorkflowDefinition definition)
    {
        List<string> errors = new();

        if (string.IsNullOrWhiteSpace(definition.WorkflowKey))
        {
            errors.Add("workflowKey is required");
        }

        if (definition.Version <= 0)
        {
            errors.Add("version must be greater than zero");
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            errors.Add("name is required");
        }

        HashSet<string> stepKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowStepDefinition step in definition.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.StepKey))
            {
                errors.Add("stepKey is required");
            }

            if (!stepKeys.Add(step.StepKey))
            {
                errors.Add($"duplicate stepKey: {step.StepKey}");
            }

            if (string.IsNullOrWhiteSpace(step.TaskType))
            {
                errors.Add($"taskType is required for {step.StepKey}");
            }

            if (step.TaskType == "module.request")
            {
                if (!step.Config.TryGetProperty("commandMessageType", out _))
                {
                    errors.Add($"module.request {step.StepKey} missing commandMessageType");
                }

                if (!step.Config.TryGetProperty("expectedCompletedMessageType", out _))
                {
                    errors.Add($"module.request {step.StepKey} missing expectedCompletedMessageType");
                }

                if (!step.Config.TryGetProperty("expectedFailedMessageType", out _))
                {
                    errors.Add($"module.request {step.StepKey} missing expectedFailedMessageType");
                }
            }
        }

        return errors;
    }
}
