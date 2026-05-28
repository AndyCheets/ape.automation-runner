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
}

public sealed class WorkflowExecutionEngine(
    IWorkflowDefinitionRepository workflowDefinitionRepository,
    WorkflowDefinitionParser workflowDefinitionParser,
    WorkflowDefinitionValidator workflowDefinitionValidator,
    IWorkflowRunRepository workflowRunRepository,
    WorkflowTaskHandlerRegistry workflowTaskHandlerRegistry,
    ILogger<WorkflowExecutionEngine> logger
) : IWorkflowExecutionEngine
{
    public async Task StartWorkflowAsync(
        MessageEnvelope envelope,
        RunWorkflowCommand command,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(command.WorkflowKey))
        {
            logger.LogError(
                "RunWorkflow command is missing workflowKey for tenant {TenantKey} correlation {CorrelationId}",
                envelope.TenantKey,
                envelope.CorrelationId
            );
            return;
        }

        WorkflowDefinitionRecord? record = command.WorkflowVersion is int version
            ? await workflowDefinitionRepository.LoadByKeyAndVersionAsync(
                envelope.TenantKey,
                command.WorkflowKey,
                version,
                cancellationToken
            )
            : await workflowDefinitionRepository.LoadActiveByKeyAsync(
                envelope.TenantKey,
                command.WorkflowKey,
                cancellationToken
            );

        if (record is null)
        {
            logger.LogError(
                "Workflow definition not found for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion}",
                envelope.TenantKey,
                envelope.CorrelationId,
                command.WorkflowKey,
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
                envelope.TenantKey,
                envelope.CorrelationId,
                record.WorkflowKey,
                record.WorkflowVersion
            );
            return;
        }

        IReadOnlyList<string> validationErrors = workflowDefinitionValidator.Validate(definition);
        if (validationErrors.Count > 0)
        {
            logger.LogError(
                "Workflow YAML validation failed for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion}: {ValidationErrors}",
                envelope.TenantKey,
                envelope.CorrelationId,
                record.WorkflowKey,
                record.WorkflowVersion,
                string.Join("; ", validationErrors)
            );
            return;
        }

        JsonElement inputs = command.Inputs.ValueKind is JsonValueKind.Undefined
            ? JsonSerializer.Deserialize<JsonElement>("{}")
            : command.Inputs;

        long workflowRunId = await workflowRunRepository.CreateWorkflowRunAsync(
            envelope.TenantKey,
            envelope.CorrelationId,
            definition.WorkflowKey,
            definition.Version,
            inputs,
            DateTimeOffset.UtcNow,
            cancellationToken
        );

        logger.LogInformation(
            "Workflow run created for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId}",
            envelope.TenantKey,
            envelope.CorrelationId,
            definition.WorkflowKey,
            definition.Version,
            workflowRunId
        );

        WorkflowStepDefinition firstStep = definition.Steps[0];
        await workflowRunRepository.CreateWorkflowStepAsync(
            envelope.TenantKey,
            workflowRunId,
            firstStep.StepKey,
            firstStep.TaskType,
            WorkflowStepRuntimeStatus.Running,
            DateTimeOffset.UtcNow,
            cancellationToken
        );

        logger.LogInformation(
            "Workflow first step started for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId} step {WorkflowStepKey}",
            envelope.TenantKey,
            envelope.CorrelationId,
            definition.WorkflowKey,
            definition.Version,
            workflowRunId,
            firstStep.StepKey
        );

        WorkflowRunContext runContext = new(
            workflowRunId,
            envelope.TenantKey,
            envelope.CorrelationId,
            definition.WorkflowKey,
            definition.Version,
            inputs
        );

        IWorkflowTaskHandler handler = workflowTaskHandlerRegistry.GetHandler(firstStep.TaskType);
        try
        {
            await handler.HandleAsync(
                runContext,
                firstStep,
                envelope,
                new Dictionary<string, JsonElement>(),
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Workflow first step failed for tenant {TenantKey} correlation {CorrelationId} workflow {WorkflowKey} v{WorkflowVersion} run {WorkflowRunId} step {WorkflowStepKey}",
                envelope.TenantKey,
                envelope.CorrelationId,
                definition.WorkflowKey,
                definition.Version,
                workflowRunId,
                firstStep.StepKey
            );
            string failureReason = ex.Message;
            await workflowRunRepository.MarkStepFailedAsync(
                envelope.TenantKey,
                workflowRunId,
                firstStep.StepKey,
                failureReason,
                DateTimeOffset.UtcNow,
                cancellationToken
            );
            await workflowRunRepository.MarkWorkflowFailedAsync(
                envelope.TenantKey,
                workflowRunId,
                failureReason,
                DateTimeOffset.UtcNow,
                cancellationToken
            );
            throw;
        }
    }
}
