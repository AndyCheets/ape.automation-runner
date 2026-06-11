using Ape.AutomationRunner.Api.Models;
using Ape.AutomationRunner.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ape.AutomationRunner.Api;

public static class WorkflowApiEndpoints
{
    private const string TenantHeaderName = "x-ape-tenant-key";

    public static WebApplication MapAutomationRunnerApi(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "ape.automation-runner" }))
            .WithTags("Health")
            .WithSummary("Returns workflow API service health.");

        RouteGroupBuilder workflows = app.MapGroup("")
            .WithTags("Workflows");

        workflows.MapGet("", ListWorkflows)
            .WithSummary("List workflow definitions for the current tenant.")
            .Produces<IReadOnlyList<WorkflowResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        workflows.MapGet("/{workflowId:long}", GetWorkflow)
            .WithSummary("Get a workflow definition by ID for the current tenant.")
            .Produces<WorkflowResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        workflows.MapPost("", CreateWorkflow)
            .WithSummary("Create a workflow definition for the current tenant.")
            .Accepts<CreateWorkflowRequest>("application/json")
            .Produces<WorkflowResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        workflows.MapPut("/{workflowId:long}", UpdateWorkflow)
            .WithSummary("Update editable workflow definition fields for the current tenant.")
            .Accepts<UpdateWorkflowRequest>("application/json")
            .Produces<WorkflowResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        workflows.MapDelete("/{workflowId:long}", DeleteWorkflow)
            .WithSummary("Deactivate a workflow definition for the current tenant.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        workflows.MapPost("/{workflowId:long}/test", TestWorkflow)
            .WithSummary("Queue a workflow test execution for the current tenant.")
            .Accepts<TestWorkflowRequest>("application/json")
            .Produces<TestWorkflowResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListWorkflows(
        HttpRequest request,
        IWorkflowApiService workflowApiService,
        CancellationToken cancellationToken
    )
    {
        if (!TryGetTenantKey(request, out string? tenantKey, out IResult? error))
        {
            return error;
        }

        WorkflowApiResult<IReadOnlyList<WorkflowResponse>> result =
            await workflowApiService.ListAsync(tenantKey, cancellationToken);
        return ToResult(result);
    }

    private static async Task<IResult> GetWorkflow(
        long workflowId,
        HttpRequest request,
        IWorkflowApiService workflowApiService,
        CancellationToken cancellationToken
    )
    {
        if (!TryGetTenantKey(request, out string? tenantKey, out IResult? error))
        {
            return error;
        }

        WorkflowApiResult<WorkflowResponse> result =
            await workflowApiService.GetAsync(tenantKey, workflowId, cancellationToken);
        return ToResult(result);
    }

    private static async Task<IResult> CreateWorkflow(
        [FromBody] CreateWorkflowRequest createRequest,
        HttpRequest request,
        IWorkflowApiService workflowApiService,
        CancellationToken cancellationToken
    )
    {
        if (!TryGetTenantKey(request, out string? tenantKey, out IResult? error))
        {
            return error;
        }

        WorkflowApiResult<WorkflowResponse> result =
            await workflowApiService.CreateAsync(tenantKey, createRequest, cancellationToken);
        return result.Error is null
            ? Results.Created($"/{result.Value!.WorkflowId}", result.Value)
            : ToErrorResult(result.Error);
    }

    private static async Task<IResult> UpdateWorkflow(
        long workflowId,
        [FromBody] UpdateWorkflowRequest updateRequest,
        HttpRequest request,
        IWorkflowApiService workflowApiService,
        CancellationToken cancellationToken
    )
    {
        if (!TryGetTenantKey(request, out string? tenantKey, out IResult? error))
        {
            return error;
        }

        WorkflowApiResult<WorkflowResponse> result =
            await workflowApiService.UpdateAsync(tenantKey, workflowId, updateRequest, cancellationToken);
        return ToResult(result);
    }

    private static async Task<IResult> DeleteWorkflow(
        long workflowId,
        HttpRequest request,
        IWorkflowApiService workflowApiService,
        CancellationToken cancellationToken
    )
    {
        if (!TryGetTenantKey(request, out string? tenantKey, out IResult? error))
        {
            return error;
        }

        WorkflowApiResult<bool> result =
            await workflowApiService.DeactivateAsync(tenantKey, workflowId, cancellationToken);
        return result.Error is null ? Results.NoContent() : ToErrorResult(result.Error);
    }

    private static async Task<IResult> TestWorkflow(
        long workflowId,
        [FromBody] TestWorkflowRequest? testRequest,
        HttpRequest request,
        IWorkflowApiService workflowApiService,
        CancellationToken cancellationToken
    )
    {
        if (!TryGetTenantKey(request, out string? tenantKey, out IResult? error))
        {
            return error;
        }

        WorkflowApiResult<TestWorkflowResponse> result =
            await workflowApiService.TestAsync(tenantKey, workflowId, testRequest, cancellationToken);
        return result.Error is null ? Results.Accepted(null, result.Value) : ToErrorResult(result.Error);
    }

    private static bool TryGetTenantKey(
        HttpRequest request,
        out string tenantKey,
        out IResult error
    )
    {
        tenantKey = request.Headers[TenantHeaderName].FirstOrDefault() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(tenantKey))
        {
            error = Results.Empty;
            return true;
        }

        error = Results.Problem(
            title: "Tenant header is required.",
            detail: $"Pass the tenant key in the {TenantHeaderName} header.",
            statusCode: StatusCodes.Status400BadRequest
        );
        return false;
    }

    private static IResult ToResult<T>(WorkflowApiResult<T> result)
        => result.Error is null ? Results.Ok(result.Value) : ToErrorResult(result.Error);

    private static IResult ToErrorResult(WorkflowApiError error)
        => error.Type switch
        {
            WorkflowApiErrorType.Validation => Results.ValidationProblem(
                error.Errors?.ToDictionary(e => e, e => new[] { e })
                    ?? new Dictionary<string, string[]> { ["request"] = [error.Message] },
                title: error.Message
            ),
            WorkflowApiErrorType.TenantNotFound => Results.Problem(
                title: error.Message,
                statusCode: StatusCodes.Status404NotFound
            ),
            WorkflowApiErrorType.NotFound => Results.Problem(
                title: error.Message,
                statusCode: StatusCodes.Status404NotFound
            ),
            WorkflowApiErrorType.Conflict => Results.Problem(
                title: error.Message,
                statusCode: StatusCodes.Status409Conflict
            ),
            _ => Results.Problem(title: error.Message),
        };
}
