using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Ape.AutomationRunner.Api.Models;

public sealed record CreateWorkflowRequest
{
    [Required]
    public string WorkflowKey { get; init; } = string.Empty;

    public int? WorkflowVersion { get; init; }

    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public string Definition { get; init; } = string.Empty;

    public bool? IsActive { get; init; }
}

public sealed record UpdateWorkflowRequest
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public string Definition { get; init; } = string.Empty;

    public bool? IsActive { get; init; }
}

public sealed record WorkflowResponse(
    long WorkflowId,
    string WorkflowKey,
    int WorkflowVersion,
    string Name,
    string Definition,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
);

public sealed record TestWorkflowRequest
{
    public JsonElement? Input { get; init; }

    public string? Reason { get; init; }
}

public sealed record TestWorkflowResponse(
    long WorkflowId,
    string WorkflowKey,
    int WorkflowVersion,
    string CorrelationId,
    string Status,
    string Message
);
