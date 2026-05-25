using Ape.Worker.Sdk.Messaging;
using Ape.AutomationRunner.Workflows;
using Ape.AutomationRunner.Workflows.TaskHandlers;
using Microsoft.Extensions.Logging;

namespace Ape.AutomationRunner.Messaging;

public sealed class WorkflowProgressEventHandler(
    IWorkflowRunRepository workflowRunRepository,
    WorkflowEventMatcher matcher,
    ILogger<WorkflowProgressEventHandler> logger
) : IMessageHandler
{
    public string MessageType => "*";

    public async Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkflowEventCandidate> candidates =
            await workflowRunRepository.GetWaitingStepsExpectingEventAsync(
                envelope.TenantKey,
                envelope.MessageType,
                cancellationToken
            );

        if (candidates.Count == 0)
        {
            logger.LogDebug(
                "Ignoring workflow event {MessageType}: no in-progress workflow run is expecting it",
                envelope.MessageType
            );
            return;
        }

        foreach (WorkflowEventCandidate candidate in candidates)
        {
            WorkflowEventMatch match = matcher.Match(
                envelope,
                candidate.Step,
                candidate.RunContext.TenantKey,
                candidate.RunContext.CorrelationId
            );

            if (!match.IsMatch)
            {
                logger.LogDebug(
                    "Ignoring workflow event {MessageType}: run {WorkflowRunId} is expecting this message type but correlation does not match",
                    envelope.MessageType,
                    candidate.RunContext.WorkflowRunId
                );
                continue;
            }

            logger.LogInformation(
                "Workflow event {MessageType} matched run {WorkflowRunId} step {StepKey}",
                envelope.MessageType,
                candidate.RunContext.WorkflowRunId,
                match.StepKey
            );
        }
    }
}
