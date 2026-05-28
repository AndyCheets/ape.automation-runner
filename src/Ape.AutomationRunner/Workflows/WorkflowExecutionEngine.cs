using System.Text.Json;
using Ape.AutomationRunner.Workflows.TaskHandlers;
using Ape.Worker.Sdk.Messaging;
using Microsoft.Extensions.Logging;

namespace Ape.AutomationRunner.Workflows;

public interface IWorkflowExecutionEngine
{
    Task StartWorkflowAsync(
        MessageEnvelope envelope,
        RunWorkflowCommand command,
        CancellationToken cancellationToken
    );

    Task HandleResultEventAsync(MessageEnvelope envelope, CancellationToken cancellationToken);
}

public sealed class WorkflowExecutionEngine(
    IWorkflowDefinitionRepository workflowDefinitionRepository,
    WorkflowDefinitionParser workflowDefinitionParser,
    WorkflowDefinitionValidator workflowDefinitionValidator,
    IWorkflowRunRepository workflowRunRepository,
    WorkflowTaskHandlerRegistry workflowTaskHandlerRegistry,
    WorkflowEventMatcher workflowEventMatcher,
    MessageContractRegistry messageContractRegistry,
    ILogger<WorkflowExecutionEngine> logger
) : IWorkflowExecutionEngine
{
    public async Task StartWorkflowAsync(
        MessageEnvelope envelope,
        RunWorkflowCommand command,
        CancellationToken cancellationToken
    )
    {
        string? workflowKey = command.WorkflowKey;
        if (string.IsNullOrWhiteSpace(workflowKey)
            && envelope.Payload.ValueKind == JsonValueKind.Object
            && envelope.Payload.TryGetProperty("workflowKey", out JsonElement workflowKeyElement))
        {
            workflowKey = workflowKeyElement.GetString();
        }

        if (string.IsNullOrWhiteSpace(workflowKey))
        {
            logger.LogError(
                "RunWorkflow command is missing workflowKey for tenant {TenantKey} correlation {CorrelationId}",
                envelope.TenantKey,
                envelope.CorrelationId
            );
            return;
        }

        logger.LogInformation(
            "Workflow triggered for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey}",
            envelope.TenantKey,
            envelope.CorrelationId,
            workflowKey
        );

        WorkflowDefinitionRecord? record = command.WorkflowVersion is int version
            ? await workflowDefinitionRepository.LoadByKeyAndVersionAsync(
                envelope.TenantKey,
                workflowKey,
                version,
                cancellationToken
            )
            : await workflowDefinitionRepository.LoadActiveByKeyAsync(
                envelope.TenantKey,
                workflowKey,
                cancellationToken
            );

        if (record is null)
        {
            logger.LogError(
                "Workflow definition not found for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion}",
                envelope.TenantKey,
                envelope.CorrelationId,
                workflowKey,
                command.WorkflowVersion
            );
            return;
        }

        logger.LogInformation(
            "Workflow definition loaded for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion}",
            envelope.TenantKey,
            envelope.CorrelationId,
            record.WorkflowKey,
            record.WorkflowVersion
        );

        WorkflowDefinition? definition = ParseAndValidate(
            record,
            envelope.TenantKey,
            envelope.CorrelationId
        );
        if (definition is null)
        {
            return;
        }

        JsonElement triggerPayload = ResolveTriggerPayload(command, envelope.Payload);

        long workflowRunId = await workflowRunRepository.CreateWorkflowRunAsync(
            envelope.TenantKey,
            envelope.CorrelationId,
            definition.WorkflowKey,
            definition.Version,
            triggerPayload,
            DateTimeOffset.UtcNow,
            cancellationToken
        );

        logger.LogInformation(
            "Workflow correlation ID assigned for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId}",
            envelope.TenantKey,
            envelope.CorrelationId,
            definition.WorkflowKey,
            definition.Version,
            workflowRunId
        );

        WorkflowRunContext runContext = new(
            workflowRunId,
            envelope.TenantKey,
            envelope.CorrelationId,
            definition.WorkflowKey,
            definition.Version,
            triggerPayload
        );

        await ExecuteStepAsync(
            definition,
            0,
            runContext,
            envelope,
            new Dictionary<string, JsonElement>(),
            cancellationToken
        );
    }

    public async Task HandleResultEventAsync(
        MessageEnvelope envelope,
        CancellationToken cancellationToken
    )
    {
        logger.LogInformation(
            "Result event received for tenant {TenantKey} correlation {CorrelationId} event {EventMessageType}",
            envelope.TenantKey,
            envelope.CorrelationId,
            envelope.MessageType
        );

        WorkflowEventCandidate? candidate =
            await workflowRunRepository.GetWaitingWorkflowByCorrelationAsync(
                envelope.TenantKey,
                envelope.CorrelationId,
                cancellationToken
            );

        if (candidate is null)
        {
            logger.LogDebug(
                "Ignoring unmatched result event for tenant {TenantKey} correlation {CorrelationId} event {EventMessageType}: no waiting workflow run found",
                envelope.TenantKey,
                envelope.CorrelationId,
                envelope.MessageType
            );
            return;
        }

        WorkflowEventMatch match = workflowEventMatcher.Match(
            envelope,
            candidate.Step,
            candidate.RunContext.TenantKey,
            candidate.RunContext.CorrelationId
        );

        if (!match.IsMatch)
        {
            logger.LogDebug(
                "Ignoring unmatched result event for tenant {TenantKey} correlation {CorrelationId} event {EventMessageType} run {WorkflowRunId} step {StepId}: expected {SuccessEventMessageType} or {FailureEventMessageType}",
                envelope.TenantKey,
                envelope.CorrelationId,
                envelope.MessageType,
                candidate.RunContext.WorkflowRunId,
                candidate.Step.StepKey,
                candidate.Step.ExpectedCompletedMessageType,
                candidate.Step.ExpectedFailedMessageType
            );
            return;
        }

        logger.LogInformation(
            "Event matched to workflow and step for tenant {TenantKey} correlation {CorrelationId} event {EventMessageType} run {WorkflowRunId} step {StepId}",
            envelope.TenantKey,
            envelope.CorrelationId,
            envelope.MessageType,
            candidate.RunContext.WorkflowRunId,
            candidate.Step.StepKey
        );

        if (match.IsFailure)
        {
            string reason = ExtractFailureReason(envelope.Payload);
            await workflowRunRepository.MarkStepFailedAsync(
                envelope.TenantKey,
                candidate.RunContext.WorkflowRunId,
                candidate.Step.StepKey,
                reason,
                DateTimeOffset.UtcNow,
                cancellationToken
            );
            await workflowRunRepository.MarkWorkflowFailedAsync(
                envelope.TenantKey,
                candidate.RunContext.WorkflowRunId,
                reason,
                DateTimeOffset.UtcNow,
                cancellationToken
            );
            logger.LogError(
                "Workflow failed for tenant {TenantKey} correlation {CorrelationId} event {EventMessageType} run {WorkflowRunId} step {StepId}: {FailureReason}",
                envelope.TenantKey,
                envelope.CorrelationId,
                envelope.MessageType,
                candidate.RunContext.WorkflowRunId,
                candidate.Step.StepKey,
                reason
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(candidate.Step.CommandMessageType))
        {
            throw new InvalidOperationException(
                $"Waiting step {candidate.Step.StepKey} does not have a command message type."
            );
        }

        MessageContract contract = messageContractRegistry.Get(candidate.Step.CommandMessageType);
        JsonElement outputs = messageContractRegistry.MapOutputs(contract, envelope.Payload);
        await workflowRunRepository.MarkStepCompletedAsync(
            envelope.TenantKey,
            candidate.RunContext.WorkflowRunId,
            candidate.Step.StepKey,
            outputs,
            DateTimeOffset.UtcNow,
            cancellationToken
        );

        logger.LogInformation(
            "Step outputs mapped for tenant {TenantKey} correlation {CorrelationId} event {EventMessageType} run {WorkflowRunId} step {StepId}",
            envelope.TenantKey,
            envelope.CorrelationId,
            envelope.MessageType,
            candidate.RunContext.WorkflowRunId,
            candidate.Step.StepKey
        );

        WorkflowDefinitionRecord? record =
            await workflowDefinitionRepository.LoadByKeyAndVersionAsync(
                envelope.TenantKey,
                candidate.RunContext.WorkflowKey,
                candidate.RunContext.WorkflowVersion,
                cancellationToken
            );
        if (record is null)
        {
            string reason = "Workflow definition not found while resuming workflow.";
            await workflowRunRepository.MarkWorkflowFailedAsync(
                envelope.TenantKey,
                candidate.RunContext.WorkflowRunId,
                reason,
                DateTimeOffset.UtcNow,
                cancellationToken
            );
            return;
        }

        WorkflowDefinition? definition = ParseAndValidate(
            record,
            envelope.TenantKey,
            envelope.CorrelationId
        );
        if (definition is null)
        {
            await workflowRunRepository.MarkWorkflowFailedAsync(
                envelope.TenantKey,
                candidate.RunContext.WorkflowRunId,
                "Workflow definition failed validation while resuming workflow.",
                DateTimeOffset.UtcNow,
                cancellationToken
            );
            return;
        }

        int currentStepIndex = FindStepIndex(definition, candidate.Step.StepKey);
        if (currentStepIndex < 0 || currentStepIndex == definition.Steps.Count - 1)
        {
            await workflowRunRepository.MarkWorkflowCompletedAsync(
                envelope.TenantKey,
                candidate.RunContext.WorkflowRunId,
                DateTimeOffset.UtcNow,
                cancellationToken
            );
            logger.LogInformation(
                "Workflow completed for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId}",
                envelope.TenantKey,
                envelope.CorrelationId,
                candidate.RunContext.WorkflowKey,
                candidate.RunContext.WorkflowVersion,
                candidate.RunContext.WorkflowRunId
            );
            return;
        }

        Dictionary<string, JsonElement> stepOutputs =
            new(await workflowRunRepository.GetCompletedStepOutputsAsync(
                envelope.TenantKey,
                candidate.RunContext.WorkflowRunId,
                cancellationToken
            ), StringComparer.Ordinal);
        stepOutputs[candidate.Step.StepKey] = outputs;

        await ExecuteStepAsync(
            definition,
            currentStepIndex + 1,
            candidate.RunContext,
            envelope,
            stepOutputs,
            cancellationToken
        );
    }

    private async Task ExecuteStepAsync(
        WorkflowDefinition definition,
        int stepIndex,
        WorkflowRunContext runContext,
        MessageEnvelope causeEnvelope,
        IReadOnlyDictionary<string, JsonElement> stepOutputs,
        CancellationToken cancellationToken
    )
    {
        WorkflowStepDefinition step = definition.Steps[stepIndex];
        await workflowRunRepository.CreateWorkflowStepAsync(
            runContext.TenantKey,
            runContext.WorkflowRunId,
            step.StepKey,
            step.TaskType,
            WorkflowStepRuntimeStatus.Running,
            DateTimeOffset.UtcNow,
            cancellationToken
        );

        logger.LogInformation(
            "Step started for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId} step {StepId}",
            runContext.TenantKey,
            runContext.CorrelationId,
            runContext.WorkflowKey,
            runContext.WorkflowVersion,
            runContext.WorkflowRunId,
            step.StepKey
        );

        IWorkflowTaskHandler handler = workflowTaskHandlerRegistry.GetHandler(step.TaskType);
        try
        {
            WorkflowStepRuntimeStatus status = await handler.HandleAsync(
                runContext,
                step,
                causeEnvelope,
                stepOutputs,
                cancellationToken
            );

            if (status == WorkflowStepRuntimeStatus.WaitingForEvent
                || status == WorkflowStepRuntimeStatus.Waiting)
            {
                logger.LogInformation(
                    "Workflow waiting for event for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId} step {StepId}",
                    runContext.TenantKey,
                    runContext.CorrelationId,
                    runContext.WorkflowKey,
                    runContext.WorkflowVersion,
                    runContext.WorkflowRunId,
                    step.StepKey
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Workflow step failed for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId} step {StepId}",
                runContext.TenantKey,
                runContext.CorrelationId,
                runContext.WorkflowKey,
                runContext.WorkflowVersion,
                runContext.WorkflowRunId,
                step.StepKey
            );
            string failureReason = ex.Message;
            await workflowRunRepository.MarkStepFailedAsync(
                runContext.TenantKey,
                runContext.WorkflowRunId,
                step.StepKey,
                failureReason,
                DateTimeOffset.UtcNow,
                cancellationToken
            );
            await workflowRunRepository.MarkWorkflowFailedAsync(
                runContext.TenantKey,
                runContext.WorkflowRunId,
                failureReason,
                DateTimeOffset.UtcNow,
                cancellationToken
            );
        }
    }

    private WorkflowDefinition? ParseAndValidate(
        WorkflowDefinitionRecord record,
        string tenantKey,
        string correlationId
    )
    {
        WorkflowDefinition definition;
        try
        {
            definition = workflowDefinitionParser.Parse(record.YamlContent);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Workflow YAML parsing failed for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion}",
                tenantKey,
                correlationId,
                record.WorkflowKey,
                record.WorkflowVersion
            );
            return null;
        }

        IReadOnlyList<string> validationErrors = workflowDefinitionValidator.Validate(definition);
        if (validationErrors.Count > 0)
        {
            logger.LogError(
                "Workflow YAML validation failed for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion}: {ValidationErrors}",
                tenantKey,
                correlationId,
                record.WorkflowKey,
                record.WorkflowVersion,
                string.Join("; ", validationErrors)
            );
            return null;
        }

        logger.LogInformation(
            "Workflow validation passed for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion}",
            tenantKey,
            correlationId,
            definition.WorkflowKey,
            definition.Version
        );
        return definition;
    }

    private static JsonElement ResolveTriggerPayload(
        RunWorkflowCommand command,
        JsonElement envelopePayload
    )
    {
        if (command.Inputs.ValueKind is not JsonValueKind.Undefined
            and not JsonValueKind.Null)
        {
            return command.Inputs.Clone();
        }

        return envelopePayload.ValueKind is JsonValueKind.Undefined
            ? JsonSerializer.Deserialize<JsonElement>("{}")
            : envelopePayload.Clone();
    }

    private static int FindStepIndex(WorkflowDefinition definition, string stepKey)
    {
        for (int i = 0; i < definition.Steps.Count; i++)
        {
            if (string.Equals(definition.Steps[i].StepKey, stepKey, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string ExtractFailureReason(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("errorMessage", out JsonElement errorMessage)
                && errorMessage.ValueKind == JsonValueKind.String)
            {
                return errorMessage.GetString() ?? "Workflow step failed.";
            }

            if (payload.TryGetProperty("errorCode", out JsonElement errorCode)
                && errorCode.ValueKind == JsonValueKind.String)
            {
                return errorCode.GetString() ?? "Workflow step failed.";
            }
        }

        return "Workflow step failed.";
    }
}
