namespace Ape.AutomationRunner.Workflows.TaskHandlers;

public sealed class WorkflowTaskHandlerRegistry(IEnumerable<IWorkflowTaskHandler> handlers)
{
    private readonly IReadOnlyDictionary<string, IWorkflowTaskHandler> _handlers =
        handlers.ToDictionary(h => h.TaskType, StringComparer.OrdinalIgnoreCase);

    public IWorkflowTaskHandler GetHandler(string taskType)
        => _handlers.TryGetValue(taskType, out IWorkflowTaskHandler? handler)
            ? handler
            : throw new InvalidOperationException(
                $"No workflow task handler is registered for task type {taskType}."
            );
}
