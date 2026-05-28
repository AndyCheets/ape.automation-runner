namespace Ape.AutomationRunner.Workflows;

public sealed class WorkflowDefinitionValidator(MessageContractRegistry? messageContractRegistry = null)
{
    private readonly MessageContractRegistry _messageContractRegistry =
        messageContractRegistry ?? new MessageContractRegistry();

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
                errors.Add("step id is required");
            }

            if (!stepKeys.Add(step.StepKey))
            {
                errors.Add($"duplicate step id: {step.StepKey}");
            }

            if (step.TaskType != "command")
            {
                errors.Add($"unsupported step type for {step.StepKey}: {step.TaskType}");
                continue;
            }

            if (step.Config is not CommandWorkflowTaskConfig command)
            {
                errors.Add($"command step {step.StepKey} has invalid config");
                continue;
            }

            if (string.IsNullOrWhiteSpace(command.MessageType))
            {
                errors.Add($"command step {step.StepKey} missing messageType");
            }
            else if (!_messageContractRegistry.IsRegistered(command.MessageType))
            {
                errors.Add($"unknown messageType for {step.StepKey}: {command.MessageType}");
            }

            if (!command.PayloadWasPresent)
            {
                errors.Add($"command step {step.StepKey} payload is required");
            }
            else if (command.Payload.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                errors.Add($"command step {step.StepKey} payload must be an object");
            }
        }

        return errors;
    }
}
