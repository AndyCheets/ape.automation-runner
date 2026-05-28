using System.Text.Json;

namespace Ape.AutomationRunner.Workflows;

public sealed record WorkflowDefinition(
    string WorkflowKey,
    int Version,
    string Name,
    IReadOnlyList<WorkflowStepDefinition> Steps
);

public sealed record WorkflowStepDefinition(
    string Id,
    string Type,
    int? TimeoutSeconds,
    WorkflowTaskConfig Config
)
{
    public string StepKey => Id;
    public string TaskType => Type;
}

public abstract record WorkflowTaskConfig(string TaskType);

public sealed record CommandWorkflowTaskConfig(
    string MessageType,
    JsonElement Payload,
    bool PayloadWasPresent = true
) : WorkflowTaskConfig("command");

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
    WaitingForEvent,
    Completed,
    Failed,
    TimedOut,
    Skipped,
}

public sealed record WorkflowStepRuntimeState(
    long WorkflowRunId,
    string Id,
    string Type,
    WorkflowStepRuntimeStatus Status,
    string? CommandMessageType,
    string? ExpectedCompletedMessageType,
    string? ExpectedFailedMessageType,
    string? CommandMessageId,
    JsonElement? Outputs = null
)
{
    public string StepKey => Id;
    public string TaskType => Type;
}

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
