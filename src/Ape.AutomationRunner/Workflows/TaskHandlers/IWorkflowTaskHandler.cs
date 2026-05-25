using Ape.Worker.Sdk.Messaging;
namespace Ape.AutomationRunner.Workflows.TaskHandlers;
public interface IWorkflowTaskHandler { string TaskType { get; } Task<WorkflowStepRuntimeStatus> HandleAsync(WorkflowRunContext runContext, WorkflowStepDefinition step, MessageEnvelope causeEnvelope, IReadOnlyDictionary<string, System.Text.Json.JsonElement> stepOutputs, CancellationToken cancellationToken); }
