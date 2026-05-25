using System.Text.Json;

namespace Ape.AutomationRunner.Workflows;

public sealed record WorkflowDefinition(
    string WorkflowKey,
    int Version,
    string Name,
    IReadOnlyList<WorkflowStepDefinition> Steps
);

public sealed record WorkflowStepDefinition(
    string StepKey,
    string TaskType,
    int? TimeoutSeconds,
    WorkflowTaskConfig Config
);

public abstract record WorkflowTaskConfig(string TaskType);

public sealed record ModuleRequestWorkflowTaskConfig(
    string CommandMessageType,
    string ExpectedCompletedMessageType,
    string ExpectedFailedMessageType,
    JsonElement Payload
) : WorkflowTaskConfig("module.request");

public sealed record UnknownWorkflowTaskConfig(
    string UnknownTaskType,
    JsonElement RawConfig
) : WorkflowTaskConfig(UnknownTaskType);

public enum WorkflowStepRuntimeStatus
{
    Pending,
    Running,
    Waiting,
    Completed,
    Failed,
    TimedOut,
}

public sealed record WorkflowStepRuntimeState(
    long WorkflowRunId,
    string StepKey,
    string TaskType,
    WorkflowStepRuntimeStatus Status,
    string? ExpectedCompletedMessageType,
    string? ExpectedFailedMessageType,
    string? CommandMessageId
);

public sealed record WorkflowRunContext(
    long WorkflowRunId,
    string TenantKey,
    string CorrelationId,
    string WorkflowKey,
    int WorkflowVersion,
    JsonElement Inputs
);

public sealed record WorkflowEventCandidate(
    WorkflowRunContext RunContext,
    WorkflowStepRuntimeState Step
);

public sealed record WorkflowEventMatch(
    bool IsMatch,
    bool IsFailure,
    string StepKey
);
