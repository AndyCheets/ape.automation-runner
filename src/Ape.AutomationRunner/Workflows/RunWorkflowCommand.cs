using System.Text.Json;

namespace Ape.AutomationRunner.Workflows;

public sealed record RunWorkflowCommand(
    string? WorkflowKey,
    int? WorkflowVersion,
    JsonElement Inputs
);
