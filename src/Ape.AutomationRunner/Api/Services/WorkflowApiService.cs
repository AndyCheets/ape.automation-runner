using System.Text.Json;
using Ape.AutomationRunner.Api.Models;
using Ape.AutomationRunner.Workflows;
using Ape.Worker.Sdk.Configuration;
using Ape.Worker.Sdk.Messaging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Ape.AutomationRunner.Api.Services;

public enum WorkflowApiErrorType
{
    NotFound,
    TenantNotFound,
    Validation,
    Conflict,
}

public sealed record WorkflowApiError(WorkflowApiErrorType Type, string Message, IReadOnlyList<string>? Errors = null);

public sealed record WorkflowApiResult<T>(T? Value, WorkflowApiError? Error)
{
    public static WorkflowApiResult<T> Success(T value) => new(value, null);

    public static WorkflowApiResult<T> Failure(WorkflowApiError error) => new(default, error);
}

public interface IWorkflowApiService
{
    Task<WorkflowApiResult<IReadOnlyList<WorkflowResponse>>> ListAsync(
        string tenantKey,
        CancellationToken cancellationToken
    );

    Task<WorkflowApiResult<WorkflowResponse>> GetAsync(
        string tenantKey,
        long workflowId,
        CancellationToken cancellationToken
    );

    Task<WorkflowApiResult<WorkflowResponse>> CreateAsync(
        string tenantKey,
        CreateWorkflowRequest request,
        CancellationToken cancellationToken
    );

    Task<WorkflowApiResult<WorkflowResponse>> UpdateAsync(
        string tenantKey,
        long workflowId,
        UpdateWorkflowRequest request,
        CancellationToken cancellationToken
    );

    Task<WorkflowApiResult<bool>> DeactivateAsync(
        string tenantKey,
        long workflowId,
        CancellationToken cancellationToken
    );

    Task<WorkflowApiResult<TestWorkflowResponse>> TestAsync(
        string tenantKey,
        long workflowId,
        TestWorkflowRequest? request,
        CancellationToken cancellationToken
    );
}

public sealed class WorkflowApiService(
    IWorkflowDefinitionRepository workflowDefinitionRepository,
    WorkflowDefinitionParser workflowDefinitionParser,
    IMessagePublisher messagePublisher,
    IOptions<ServiceIdentityOptions> serviceIdentityOptions
) : IWorkflowApiService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<WorkflowApiResult<IReadOnlyList<WorkflowResponse>>> ListAsync(
        string tenantKey,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IReadOnlyList<WorkflowDefinitionRecord> records =
                await workflowDefinitionRepository.ListAsync(tenantKey, cancellationToken);
            return WorkflowApiResult<IReadOnlyList<WorkflowResponse>>.Success(
                records.Select(ToResponse).ToArray()
            );
        }
        catch (TenantDatabaseNotFoundException ex)
        {
            return TenantNotFound<IReadOnlyList<WorkflowResponse>>(ex.TenantKey);
        }
    }

    public async Task<WorkflowApiResult<WorkflowResponse>> GetAsync(
        string tenantKey,
        long workflowId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            WorkflowDefinitionRecord? record = await workflowDefinitionRepository.LoadByIdAsync(
                tenantKey,
                workflowId,
                cancellationToken
            );

            return record is null
                ? NotFound<WorkflowResponse>(workflowId)
                : WorkflowApiResult<WorkflowResponse>.Success(ToResponse(record));
        }
        catch (TenantDatabaseNotFoundException ex)
        {
            return TenantNotFound<WorkflowResponse>(ex.TenantKey);
        }
    }

    public async Task<WorkflowApiResult<WorkflowResponse>> CreateAsync(
        string tenantKey,
        CreateWorkflowRequest request,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<string> errors = ValidateCreate(request);
        if (errors.Count > 0)
        {
            return Validation<WorkflowResponse>(errors);
        }

        try
        {
            WorkflowDefinitionRecord record = await workflowDefinitionRepository.CreateAsync(
                tenantKey,
                request.WorkflowKey.Trim(),
                request.WorkflowVersion ?? 1,
                request.Name.Trim(),
                request.Definition,
                request.IsActive ?? true,
                cancellationToken
            );

            return WorkflowApiResult<WorkflowResponse>.Success(ToResponse(record));
        }
        catch (TenantDatabaseNotFoundException ex)
        {
            return TenantNotFound<WorkflowResponse>(ex.TenantKey);
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            return WorkflowApiResult<WorkflowResponse>.Failure(
                new WorkflowApiError(
                    WorkflowApiErrorType.Conflict,
                    "A workflow definition with the same workflow key and version already exists."
                )
            );
        }
    }

    public async Task<WorkflowApiResult<WorkflowResponse>> UpdateAsync(
        string tenantKey,
        long workflowId,
        UpdateWorkflowRequest request,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<string> errors = ValidateUpdate(request);
        if (errors.Count > 0)
        {
            return Validation<WorkflowResponse>(errors);
        }

        try
        {
            WorkflowDefinitionRecord? existing = await workflowDefinitionRepository.LoadByIdAsync(
                tenantKey,
                workflowId,
                cancellationToken
            );
            if (existing is null)
            {
                return NotFound<WorkflowResponse>(workflowId);
            }

            WorkflowDefinitionRecord? updated = await workflowDefinitionRepository.UpdateAsync(
                tenantKey,
                workflowId,
                request.Name.Trim(),
                request.Definition,
                request.IsActive ?? existing.IsActive,
                cancellationToken
            );

            return updated is null
                ? NotFound<WorkflowResponse>(workflowId)
                : WorkflowApiResult<WorkflowResponse>.Success(ToResponse(updated));
        }
        catch (TenantDatabaseNotFoundException ex)
        {
            return TenantNotFound<WorkflowResponse>(ex.TenantKey);
        }
    }

    public async Task<WorkflowApiResult<bool>> DeactivateAsync(
        string tenantKey,
        long workflowId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            bool deactivated = await workflowDefinitionRepository.DeactivateAsync(
                tenantKey,
                workflowId,
                cancellationToken
            );

            return deactivated
                ? WorkflowApiResult<bool>.Success(true)
                : NotFound<bool>(workflowId);
        }
        catch (TenantDatabaseNotFoundException ex)
        {
            return TenantNotFound<bool>(ex.TenantKey);
        }
    }

    public async Task<WorkflowApiResult<TestWorkflowResponse>> TestAsync(
        string tenantKey,
        long workflowId,
        TestWorkflowRequest? request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            WorkflowDefinitionRecord? record = await workflowDefinitionRepository.LoadByIdAsync(
                tenantKey,
                workflowId,
                cancellationToken
            );
            if (record is null)
            {
                return NotFound<TestWorkflowResponse>(workflowId);
            }

            if (!record.IsActive)
            {
                return Validation<TestWorkflowResponse>(["workflow definition is inactive"]);
            }

            string correlationId = Guid.NewGuid().ToString("N");
            JsonElement input = request?.Input ?? JsonSerializer.Deserialize<JsonElement>("{}");
            RunWorkflowCommand command = new(record.WorkflowKey, record.WorkflowVersion, input);
            JsonElement payload = JsonSerializer.SerializeToElement(command, SerializerOptions);
            Dictionary<string, string> metadata = new()
            {
                ["workflowId"] = record.Id.ToString(),
                ["workflowKey"] = record.WorkflowKey,
                ["workflowVersion"] = record.WorkflowVersion.ToString(),
            };
            if (!string.IsNullOrWhiteSpace(request?.Reason))
            {
                metadata["reason"] = request.Reason;
            }

            MessageEnvelope envelope = new(
                Guid.NewGuid().ToString("N"),
                correlationId,
                null,
                tenantKey,
                ResolveSource(),
                "RunWorkflow",
                1,
                DateTimeOffset.UtcNow,
                metadata,
                payload
            );

            await messagePublisher.PublishCommandAsync(envelope, cancellationToken);

            return WorkflowApiResult<TestWorkflowResponse>.Success(
                new TestWorkflowResponse(
                    record.Id,
                    record.WorkflowKey,
                    record.WorkflowVersion,
                    correlationId,
                    "queued",
                    "Workflow execution was requested."
                )
            );
        }
        catch (TenantDatabaseNotFoundException ex)
        {
            return TenantNotFound<TestWorkflowResponse>(ex.TenantKey);
        }
    }

    private IReadOnlyList<string> ValidateCreate(CreateWorkflowRequest request)
    {
        List<string> errors = ValidateDefinition(request.Name, request.Definition);
        if (string.IsNullOrWhiteSpace(request.WorkflowKey))
        {
            errors.Add("workflowKey is required");
        }

        if (request.WorkflowVersion is <= 0)
        {
            errors.Add("workflowVersion must be greater than zero");
        }

        return errors;
    }

    private IReadOnlyList<string> ValidateUpdate(UpdateWorkflowRequest request)
        => ValidateDefinition(request.Name, request.Definition);

    private List<string> ValidateDefinition(string name, string definition)
    {
        List<string> errors = new();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("name is required");
        }

        if (string.IsNullOrWhiteSpace(definition))
        {
            errors.Add("definition is required");
            return errors;
        }

        try
        {
            WorkflowDefinition parsed = workflowDefinitionParser.Parse(definition);
            if (string.IsNullOrWhiteSpace(parsed.WorkflowKey))
            {
                errors.Add("definition workflowKey is required");
            }

            if (parsed.Version <= 0)
            {
                errors.Add("definition version must be greater than zero");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or YamlDotNet.Core.YamlException)
        {
            errors.Add($"definition must be valid workflow YAML: {ex.Message}");
        }

        return errors;
    }

    private string ResolveSource()
        => string.IsNullOrWhiteSpace(serviceIdentityOptions.Value.Source)
            ? "ape.automation-runner.api"
            : serviceIdentityOptions.Value.Source;

    private static WorkflowResponse ToResponse(WorkflowDefinitionRecord record)
        => new(
            record.Id,
            record.WorkflowKey,
            record.WorkflowVersion,
            record.Name,
            record.YamlContent,
            record.IsActive,
            record.CreatedAtUtc,
            record.UpdatedAtUtc
        );

    private static WorkflowApiResult<T> Validation<T>(IReadOnlyList<string> errors)
        => WorkflowApiResult<T>.Failure(
            new WorkflowApiError(WorkflowApiErrorType.Validation, "The request is invalid.", errors)
        );

    private static WorkflowApiResult<T> NotFound<T>(long workflowId)
        => WorkflowApiResult<T>.Failure(
            new WorkflowApiError(WorkflowApiErrorType.NotFound, $"Workflow definition {workflowId} was not found.")
        );

    private static WorkflowApiResult<T> TenantNotFound<T>(string tenantKey)
        => WorkflowApiResult<T>.Failure(
            new WorkflowApiError(WorkflowApiErrorType.TenantNotFound, $"Tenant {tenantKey} was not found.")
        );
}
