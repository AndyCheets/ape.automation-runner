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

        if (definition.Steps.Count == 0)
        {
            errors.Add("at least one step is required");
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
                if (step.Config is not ModuleRequestWorkflowTaskConfig moduleRequest)
                {
                    errors.Add($"module.request {step.StepKey} has invalid config");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(moduleRequest.CommandMessageType))
                {
                    errors.Add($"module.request {step.StepKey} missing commandMessageType");
                }

                if (string.IsNullOrWhiteSpace(moduleRequest.ExpectedCompletedMessageType))
                {
                    errors.Add($"module.request {step.StepKey} missing expectedCompletedMessageType");
                }

                if (string.IsNullOrWhiteSpace(moduleRequest.ExpectedFailedMessageType))
                {
                    errors.Add($"module.request {step.StepKey} missing expectedFailedMessageType");
                }
            }
        }

        return errors;
    }
}
