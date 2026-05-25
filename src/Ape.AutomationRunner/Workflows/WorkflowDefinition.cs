using System.Text.Json;
namespace Ape.AutomationRunner.Workflows;
public sealed record WorkflowDefinition(string WorkflowKey,int Version,string Name,IReadOnlyList<WorkflowStepDefinition> Steps);
public sealed record WorkflowStepDefinition(string StepKey,string TaskType,int? TimeoutSeconds,JsonElement Config);
public enum WorkflowStepRuntimeStatus { Pending, Running, Waiting, Completed, Failed, TimedOut }
public sealed record WorkflowStepRuntimeState(long WorkflowRunId,string StepKey,string TaskType,WorkflowStepRuntimeStatus Status,string? ExpectedCompletedMessageType,string? ExpectedFailedMessageType,string? CommandMessageId);
public sealed record WorkflowRunContext(long WorkflowRunId,string TenantKey,string CorrelationId,string WorkflowKey,int WorkflowVersion,JsonElement Inputs);
public sealed record WorkflowEventMatch(bool IsMatch,bool IsFailure,string StepKey);
