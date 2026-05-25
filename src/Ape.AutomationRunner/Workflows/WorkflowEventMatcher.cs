using Ape.Worker.Sdk.Messaging;

namespace Ape.AutomationRunner.Workflows;

public sealed class WorkflowEventMatcher
{
    public WorkflowEventMatch Match(
        MessageEnvelope envelope,
        WorkflowStepRuntimeState step,
        string tenantKey,
        string correlationId
    )
    {
        if (!string.Equals(envelope.TenantKey, tenantKey, StringComparison.Ordinal)
            || !string.Equals(envelope.CorrelationId, correlationId, StringComparison.Ordinal))
        {
            return new WorkflowEventMatch(false, false, step.StepKey);
        }

        if (string.Equals(
            envelope.MessageType,
            step.ExpectedCompletedMessageType,
            StringComparison.Ordinal
        ))
        {
            return new WorkflowEventMatch(true, false, step.StepKey);
        }

        if (string.Equals(
            envelope.MessageType,
            step.ExpectedFailedMessageType,
            StringComparison.Ordinal
        ))
        {
            return new WorkflowEventMatch(true, true, step.StepKey);
        }

        return new WorkflowEventMatch(false, false, step.StepKey);
    }
}
