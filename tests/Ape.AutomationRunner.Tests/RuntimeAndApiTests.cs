using System.Text.Json;
using Ape.AutomationRunner.Api;
using Ape.AutomationRunner.Api.Models;
using Ape.AutomationRunner.Api.Services;
using Ape.AutomationRunner.Runtime;
using Ape.AutomationRunner.Workflows;
using Ape.Worker.Sdk.Configuration;
using Ape.Worker.Sdk.Messaging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Ape.AutomationRunner.Tests;

public sealed class RuntimeAndApiTests
{
    private const string ValidYaml = """
        workflowKey: sample-workflow
        name: Sample Workflow
        version: 1
        steps:
          - id: send-message
            type: command
            messageType: SendTelegramMessage
            payload: {}
        """;

    [Test]
    public void ServiceMode_Missing_DefaultsToWorker()
    {
        Assert.That(ApeServiceModeResolver.Resolve(null), Is.EqualTo(ApeServiceMode.Worker));
        Assert.That(ApeServiceModeResolver.Resolve(string.Empty), Is.EqualTo(ApeServiceMode.Worker));
    }

    [Test]
    public void ServiceMode_Invalid_FailsClearly()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => ApeServiceModeResolver.Resolve("bad")
        )!;

        Assert.That(ex.Message, Does.Contain("Supported values are 'worker' and 'api'"));
    }

    [Test]
    public void ServiceMode_Api_ResolvesApi()
    {
        Assert.That(ApeServiceModeResolver.Resolve("api"), Is.EqualTo(ApeServiceMode.Api));
    }

    [Test]
    public void WorkflowApi_ResponseSerialization_UsesCamelCaseFieldNames()
    {
        WorkflowResponse response = new(
            42,
            "sample-workflow",
            1,
            "Sample Workflow",
            ValidYaml,
            true,
            DateTimeOffset.Parse("2026-06-10T14:30:00Z"),
            DateTimeOffset.Parse("2026-06-10T15:30:00Z")
        );

        string json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.That(document.RootElement.TryGetProperty("workflowId", out _), Is.True);
        Assert.That(document.RootElement.TryGetProperty("workflowKey", out _), Is.True);
        Assert.That(document.RootElement.TryGetProperty("createdAtUtc", out _), Is.True);
        Assert.That(document.RootElement.TryGetProperty("WorkflowId", out _), Is.False);
    }

    [Test]
    public void SwaggerDocumentation_UsesApeStandardPaths()
    {
        Assert.That(ApeSwaggerDocumentationExtensions.SwaggerUiPath, Is.EqualTo("/docs"));
        Assert.That(ApeSwaggerDocumentationExtensions.ReDocPath, Is.EqualTo("/redoc"));
        Assert.That(ApeSwaggerDocumentationExtensions.OpenApiPath, Is.EqualTo("/openapi.json"));
        Assert.That(ApeSwaggerDocumentationExtensions.SwaggerUiPath, Is.EqualTo("/docs"));
        Assert.That(ApeSwaggerDocumentationExtensions.SwaggerUiOpenApiPath, Is.EqualTo("../openapi.json"));
        Assert.That(ApeSwaggerDocumentationExtensions.ReDocSpecUrl, Is.EqualTo("openapi.json"));
        Assert.That(ApeSwaggerDocumentationExtensions.GetReDocHtml(), Does.Contain("spec-url=\"openapi.json\""));
    }

    [Test]
    public async Task WorkflowApi_List_CallsRepositoryForTenant()
    {
        Mock<IWorkflowDefinitionRepository> repository = RepositoryWithWorkflow();
        WorkflowApiService service = Service(repository.Object, Mock.Of<IMessagePublisher>());

        WorkflowApiResult<IReadOnlyList<WorkflowResponse>> result =
            await service.ListAsync("tenant-a", CancellationToken.None);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Value, Has.Count.EqualTo(1));
        repository.Verify(r => r.ListAsync("tenant-a", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task WorkflowApi_Get_ReturnsWorkflowById()
    {
        WorkflowApiService service = Service(RepositoryWithWorkflow().Object, Mock.Of<IMessagePublisher>());

        WorkflowApiResult<WorkflowResponse> result =
            await service.GetAsync("tenant-a", 42, CancellationToken.None);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Value!.WorkflowId, Is.EqualTo(42));
    }

    [Test]
    public async Task WorkflowApi_Create_ValidatesRequiredFields()
    {
        WorkflowApiService service = Service(RepositoryWithWorkflow().Object, Mock.Of<IMessagePublisher>());

        WorkflowApiResult<WorkflowResponse> result = await service.CreateAsync(
            "tenant-a",
            new CreateWorkflowRequest(),
            CancellationToken.None
        );

        Assert.That(result.Error?.Type, Is.EqualTo(WorkflowApiErrorType.Validation));
        Assert.That(result.Error?.Errors, Has.Some.Contains("workflowKey is required"));
        Assert.That(result.Error?.Errors, Has.Some.Contains("name is required"));
        Assert.That(result.Error?.Errors, Has.Some.Contains("definition is required"));
    }

    [Test]
    public async Task WorkflowApi_Update_ValidatesRequiredFields()
    {
        WorkflowApiService service = Service(RepositoryWithWorkflow().Object, Mock.Of<IMessagePublisher>());

        WorkflowApiResult<WorkflowResponse> result = await service.UpdateAsync(
            "tenant-a",
            42,
            new UpdateWorkflowRequest(),
            CancellationToken.None
        );

        Assert.That(result.Error?.Type, Is.EqualTo(WorkflowApiErrorType.Validation));
        Assert.That(result.Error?.Errors, Has.Some.Contains("name is required"));
        Assert.That(result.Error?.Errors, Has.Some.Contains("definition is required"));
    }

    [Test]
    public async Task WorkflowApi_Delete_DeactivatesRepositoryRecord()
    {
        Mock<IWorkflowDefinitionRepository> repository = RepositoryWithWorkflow();
        WorkflowApiService service = Service(repository.Object, Mock.Of<IMessagePublisher>());

        WorkflowApiResult<bool> result = await service.DeactivateAsync("tenant-a", 42, CancellationToken.None);

        Assert.That(result.Error, Is.Null);
        repository.Verify(r => r.DeactivateAsync("tenant-a", 42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task WorkflowApi_Test_PublishesRunWorkflowCommandAndReturnsQueuedResponse()
    {
        Mock<IMessagePublisher> publisher = new();
        WorkflowApiService service = Service(RepositoryWithWorkflow().Object, publisher.Object);

        WorkflowApiResult<TestWorkflowResponse> result = await service.TestAsync(
            "tenant-a",
            42,
            new TestWorkflowRequest
            {
                Input = JsonSerializer.Deserialize<JsonElement>("{\"recipient_id\":\"telegram-recipient\"}"),
                Reason = "Manual portal test",
            },
            CancellationToken.None
        );

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Value!.Status, Is.EqualTo("queued"));
        Assert.That(result.Value.CorrelationId, Is.Not.Empty);
        publisher.Verify(
            p => p.PublishCommandAsync(
                It.Is<MessageEnvelope>(e =>
                    e.TenantKey == "tenant-a"
                    && e.MessageType == "RunWorkflow"
                    && e.Payload.GetProperty("workflowKey").GetString() == "sample-workflow"
                    && e.Payload.GetProperty("workflowVersion").GetInt32() == 1
                    && e.Payload.GetProperty("inputs").GetProperty("recipient_id").GetString() == "telegram-recipient"),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Test]
    public async Task WorkflowApi_Test_InvalidWorkflowId_ReturnsNotFound()
    {
        Mock<IWorkflowDefinitionRepository> repository = RepositoryWithWorkflow(loadWorkflow: false);
        WorkflowApiService service = Service(repository.Object, Mock.Of<IMessagePublisher>());

        WorkflowApiResult<TestWorkflowResponse> result = await service.TestAsync(
            "tenant-a",
            404,
            null,
            CancellationToken.None
        );

        Assert.That(result.Error?.Type, Is.EqualTo(WorkflowApiErrorType.NotFound));
    }

    private static WorkflowApiService Service(
        IWorkflowDefinitionRepository repository,
        IMessagePublisher publisher
    )
        => new(
            repository,
            new WorkflowDefinitionParser(),
            publisher,
            Options.Create(new ServiceIdentityOptions { Source = "test-api" })
        );

    private static Mock<IWorkflowDefinitionRepository> RepositoryWithWorkflow(bool loadWorkflow = true)
    {
        WorkflowDefinitionRecord record = new(
            42,
            "sample-workflow",
            1,
            "Sample Workflow",
            ValidYaml,
            "hash",
            true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow
        );

        Mock<IWorkflowDefinitionRepository> repository = new();
        repository.Setup(r => r.ListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { record });
        repository.Setup(r => r.LoadByIdAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loadWorkflow ? record : null);
        repository.Setup(r => r.CreateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repository.Setup(r => r.UpdateAsync(
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repository.Setup(r => r.DeactivateAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loadWorkflow);
        return repository;
    }
}
