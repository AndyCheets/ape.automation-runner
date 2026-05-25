namespace Ape.AutomationRunner.Configuration;
public sealed class WorkflowRunnerOptions { public bool TimeoutMonitorEnabled { get; set; } = true; public int TimeoutMonitorIntervalSeconds { get; set; } = 60; public int DefaultStepTimeoutSeconds { get; set; } = 300; }
