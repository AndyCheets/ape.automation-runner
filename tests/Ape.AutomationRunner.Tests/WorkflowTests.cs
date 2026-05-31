using System.Text.Json;
using Ape.AutomationRunner.Configuration;
using Ape.AutomationRunner.Messaging;
using Ape.AutomationRunner.Workflows;
using Ape.AutomationRunner.Workflows.TaskHandlers;
using Ape.Worker.Sdk.Configuration;
using Ape.Worker.Sdk.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Ape.AutomationRunner.Tests;

public sealed class WorkflowTests
{
    private const string TwoStepYaml = """
        workflowKey: ai-telegram-test
        name: AI Telegram Test
        version: 1

        steps:
          - id: generate-message
            type: command
            messageType: GenerateTextWithAi
            payload:
              systemPrompt: "You write short operational updates for a technical platform."
              userPrompt: "Write a short message confirming that the APE two-step workflow has successfully generated AI text and passed it to a Telegram message step."

          - id: send-telegram
            type: command
            messageType: SendTelegramMessage
            payload:
              recipient_id: "{{ trigger.payload.recipient_id }}"
              contentSource:
                type: ai-response
                id: "{{ steps.generate-message.outputs.aiResponseId }}"
        """;

    [Test]
    public void Validate_ValidTwoStepWorkflow_Passes()
    {
        WorkflowDefinition definition = Parse(TwoStepYaml);
        Assert.That(Validator().Validate(definition), Is.Empty);
    }

    [Test]
    public void Validate_DuplicateStepIds_Fails()
    {
        const string yaml = """
            workflowKey: k
            name: n
            version: 1
            steps:
              - id: a
                type: command
                messageType: SendTelegramMessage
                payload: {}
              - id: a
                type: command
                messageType: SendTelegramMessage
                payload: {}
            """;

        Assert.That(Validator().Validate(Parse(yaml)), Has.Some.Contains("duplicate"));
    }

    [Test]
    public void Validate_UnsupportedStepType_Fails()
    {
        const string yaml = """
            workflowKey: k
            name: n
            version: 1
            steps:
              - id: a
                type: branch
                messageType: SendTelegramMessage
                payload: {}
            """;

        Assert.That(Validator().Validate(Parse(yaml)), Has.Some.Contains("unsupported step type"));
    }

    [Test]
    public void Validate_UnknownMessageType_Fails()
    {
        const string yaml = """
            workflowKey: k
            name: n
            version: 1
            steps:
              - id: a
                type: command
                messageType: Nope
                payload: {}
            """;

        Assert.That(Validator().Validate(Parse(yaml)), Has.Some.Contains("unknown messageType"));
    }

    [Test]
    public void Renderer_TriggerPayloadPlaceholder_Resolves()
    {
        JsonElement rendered = Renderer().Render(
            Json("""{"recipient_id":"{{ trigger.payload.recipient_id }}"}"""),
            Json("""{"recipient_id":"telegram-recipient"}"""),
            new Dictionary<string, JsonElement>(),
            "corr"
        );

        Assert.That(rendered.GetProperty("recipient_id").GetString(), Is.EqualTo("telegram-recipient"));
    }

    [Test]
    public void Renderer_PreviousStepOutputPlaceholder_Resolves()
    {
        Dictionary<string, JsonElement> outputs = new()
        {
            ["generate-message"] = Json("""{"aiResponseId":"ai-response-123"}"""),
        };

        JsonElement rendered = Renderer().Render(
            Json("""{"contentSource":{"type":"ai-response","id":"{{ steps.generate-message.outputs.aiResponseId }}"}}"""),
            Json("{}"),
            outputs,
            "corr"
        );

        Assert.That(
            rendered.GetProperty("contentSource").GetProperty("id").GetString(),
            Is.EqualTo("ai-response-123")
        );
    }

    [Test]
    public void Renderer_CorrelationIdPlaceholder_Resolves()
    {
        JsonElement rendered = Renderer().Render(
            Json("""{"id":"{{ correlationId }}"}"""),
            Json("{}"),
            new Dictionary<string, JsonElement>(),
            "corr-123"
        );

        Assert.That(rendered.GetProperty("id").GetString(), Is.EqualTo("corr-123"));
    }

    [Test]
    public void Renderer_MissingTemplateValue_FailsClearly()
    {
        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
            Renderer().Render(
                Json("""{"x":"{{ trigger.payload.missing }}"}"""),
                Json("{}"),
                new Dictionary<string, JsonElement>(),
                "corr"
            )
        );

        Assert.That(ex!.Message, Does.Contain("Missing value"));
    }

    [Test]
    public void ContractRegistry_AiTextGenerated_MapsAiResponseMetadata()
    {
        MessageContractRegistry registry = new();
        JsonElement outputs = registry.MapOutputs(
            registry.Get("GenerateTextWithAi"),
            Json(
                """
                {
                  "aiResponseId": "9b2f8c5f-2f5d-4fd8-b1d9-8b8cf0d6c111",
                  "promptKey": "weekly-email-report",
                  "promptVersion": 1,
                  "model": "gpt-4.1",
                  "status": "completed"
                }
                """
            )
        );

        Assert.That(outputs.GetProperty("aiResponseId").GetString(), Is.EqualTo("9b2f8c5f-2f5d-4fd8-b1d9-8b8cf0d6c111"));
        Assert.That(outputs.GetProperty("promptKey").GetString(), Is.EqualTo("weekly-email-report"));
        Assert.That(outputs.GetProperty("promptVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(outputs.GetProperty("model").GetString(), Is.EqualTo("gpt-4.1"));
        Assert.That(outputs.GetProperty("status").GetString(), Is.EqualTo("completed"));
    }

    [Test]
    public async Task StartWorkflow_PublishesGenerateTextWithAi_WithWorkflowCorrelation()
    {
        FakeWorkflowRunRepository runs = new();
        Mock<IMessagePublisher> publisher = new();
        WorkflowExecutionEngine engine = Engine(runs, publisher.Object);

        await engine.StartWorkflowAsync(
            Env("RunWorkflow", "tenant", "corr", """{"workflowKey":"ai-telegram-test","recipient_id":"telegram-recipient"}"""),
            new RunWorkflowCommand("ai-telegram-test", 1, default),
            CancellationToken.None
        );

        Assert.That(runs.CreatedRun?.CorrelationId, Is.EqualTo("corr"));
        publisher.Verify(
            p => p.PublishCommandAsync(
                It.Is<MessageEnvelope>(e =>
                    e.MessageType == "GenerateTextWithAi"
                    && e.CorrelationId == "corr"
                    && e.TenantKey == "tenant"),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
        Assert.That(runs.WaitingStep?.ExpectedCompletedMessageType, Is.EqualTo("AiTextGenerated"));
    }

    [Test]
    public async Task StartWorkflow_WithEmptyInputs_PreservesTopLevelTriggerFields()
    {
        FakeWorkflowRunRepository runs = new();
        WorkflowExecutionEngine engine = Engine(runs, Mock.Of<IMessagePublisher>());

        await engine.StartWorkflowAsync(
            Env(
                "RunWorkflow",
                "tenant",
                "corr",
                """{"workflowKey":"ai-telegram-test","inputs":{},"recipient_id":"telegram-recipient"}"""
            ),
            new RunWorkflowCommand("ai-telegram-test", 1, Json("""{}""")),
            CancellationToken.None
        );
        await engine.HandleResultEventAsync(
            AiTextGenerated("tenant", "corr"),
            CancellationToken.None
        );

        Assert.That(runs.FailedWorkflowReason, Is.Null);
        Assert.That(runs.WaitingStep?.StepKey, Is.EqualTo("send-telegram"));
        Assert.That(
            runs.WaitingStep?.InputPayload.GetProperty("recipient_id").GetString(),
            Is.EqualTo("telegram-recipient")
        );
    }

    [Test]
    public async Task SuccessEvent_ResumesWorkflow_AndStartsNextStep()
    {
        FakeWorkflowRunRepository runs = await StartedRunAsync();
        Mock<IMessagePublisher> publisher = new();
        WorkflowExecutionEngine engine = Engine(runs, publisher.Object);

        await engine.HandleResultEventAsync(
            AiTextGenerated("tenant", "corr"),
            CancellationToken.None
        );

        publisher.Verify(
            p => p.PublishCommandAsync(
                It.Is<MessageEnvelope>(e =>
                    e.MessageType == "SendTelegramMessage"
                    && e.Payload.GetProperty("recipient_id").GetString() == "telegram-recipient"
                    && e.Payload.GetProperty("contentSource").GetProperty("type").GetString() == "ai-response"
                    && e.Payload.GetProperty("contentSource").GetProperty("id").GetString() == "9b2f8c5f-2f5d-4fd8-b1d9-8b8cf0d6c111"
                    && e.CorrelationId == "corr"),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Test]
    public async Task SuccessEvent_WithDifferentCasing_ResumesWorkflow()
    {
        FakeWorkflowRunRepository runs = await StartedRunAsync();
        Mock<IMessagePublisher> publisher = new();
        WorkflowExecutionEngine engine = Engine(runs, publisher.Object);

        await engine.HandleResultEventAsync(
            AiTextGenerated("tenant", "corr", "AITextGenerated"),
            CancellationToken.None
        );

        publisher.Verify(
            p => p.PublishCommandAsync(
                It.Is<MessageEnvelope>(e => e.MessageType == "SendTelegramMessage"),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Test]
    public async Task FailureEvent_MarksWorkflowFailed()
    {
        FakeWorkflowRunRepository runs = await StartedRunAsync();
        WorkflowExecutionEngine engine = Engine(runs, Mock.Of<IMessagePublisher>());

        await engine.HandleResultEventAsync(
            Env("AiTextGenerationFailed", "tenant", "corr", """{"errorMessage":"AI failed"}"""),
            CancellationToken.None
        );

        Assert.That(runs.FailedWorkflowReason, Is.EqualTo("AI failed"));
        Assert.That(runs.CompletedWorkflow, Is.False);
    }

    [Test]
    public async Task Matching_UsesTenantCorrelationAndExpectedMessageType()
    {
        FakeWorkflowRunRepository runs = await StartedRunAsync();
        WorkflowExecutionEngine engine = Engine(runs, Mock.Of<IMessagePublisher>());

        await engine.HandleResultEventAsync(Env("AiTextGenerated", "other", "corr", "{}"), CancellationToken.None);
        await engine.HandleResultEventAsync(Env("AiTextGenerated", "tenant", "wrong", "{}"), CancellationToken.None);
        await engine.HandleResultEventAsync(Env("TelegramMessageSent", "tenant", "corr", "{}"), CancellationToken.None);

        Assert.That(runs.CompletedSteps, Is.Empty);
        Assert.That(runs.FailedWorkflowReason, Is.Null);
    }

    [Test]
    public async Task FinalTelegramSuccess_CompletesWorkflow()
    {
        FakeWorkflowRunRepository runs = await StartedRunAsync();
        WorkflowExecutionEngine engine = Engine(runs, Mock.Of<IMessagePublisher>());

        await engine.HandleResultEventAsync(
            AiTextGenerated("tenant", "corr"),
            CancellationToken.None
        );
        await engine.HandleResultEventAsync(
            Env("TelegramMessageSent", "tenant", "corr", """{"status":"sent"}"""),
            CancellationToken.None
        );

        Assert.That(runs.CompletedWorkflow, Is.True);
    }

    [Test]
    public async Task ProgressHandler_DelegatesResultEventsToEngine()
    {
        Mock<IWorkflowExecutionEngine> engine = new();
        WorkflowProgressEventHandler handler = new(
            engine.Object,
            NullLogger<WorkflowProgressEventHandler>.Instance
        );
        MessageEnvelope envelope = Env("AiTextGenerated", "tenant", "corr");

        await handler.HandleAsync(envelope, CancellationToken.None);

        engine.Verify(e => e.HandleResultEventAsync(envelope, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<FakeWorkflowRunRepository> StartedRunAsync()
    {
        FakeWorkflowRunRepository runs = new();
        WorkflowExecutionEngine engine = Engine(runs, Mock.Of<IMessagePublisher>());
        await engine.StartWorkflowAsync(
            Env("RunWorkflow", "tenant", "corr", """{"workflowKey":"ai-telegram-test","recipient_id":"telegram-recipient"}"""),
            new RunWorkflowCommand("ai-telegram-test", 1, default),
            CancellationToken.None
        );
        return runs;
    }

    private static WorkflowExecutionEngine Engine(
        FakeWorkflowRunRepository runs,
        IMessagePublisher publisher
    )
    {
        MessageContractRegistry contracts = new();
        WorkflowDefinitionParser parser = new();
        WorkflowDefinitionValidator validator = new(contracts);
        WorkflowPayloadTemplateRenderer renderer = new();
        CommandWorkflowTaskHandler commandHandler = new(
            publisher,
            renderer,
            runs,
            contracts,
            Options.Create(new WorkflowRunnerOptions()),
            Options.Create(new ServiceIdentityOptions { Source = "ape.automation-runner" }),
            NullLogger<CommandWorkflowTaskHandler>.Instance
        );

        return new WorkflowExecutionEngine(
            new FakeWorkflowDefinitionRepository(TwoStepYaml),
            parser,
            validator,
            runs,
            new WorkflowTaskHandlerRegistry([commandHandler]),
            new WorkflowEventMatcher(),
            contracts,
            NullLogger<WorkflowExecutionEngine>.Instance
        );
    }

    private static WorkflowDefinition Parse(string yaml) => new WorkflowDefinitionParser().Parse(yaml);
    private static WorkflowDefinitionValidator Validator() => new(new MessageContractRegistry());
    private static WorkflowPayloadTemplateRenderer Renderer() => new();
    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static MessageEnvelope Env(
        string type,
        string tenant,
        string correlation,
        string payload = "{}"
    )
        => new(
            Guid.NewGuid().ToString("N"),
            correlation,
            null,
            tenant,
            "test",
            type,
            1,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            Json(payload)
        );

    private static MessageEnvelope AiTextGenerated(
        string tenant,
        string correlation,
        string type = "AiTextGenerated"
    )
        => Env(
            type,
            tenant,
            correlation,
            """
            {
              "aiResponseId": "9b2f8c5f-2f5d-4fd8-b1d9-8b8cf0d6c111",
              "promptKey": "weekly-email-report",
              "promptVersion": 1,
              "model": "gpt-4.1",
              "status": "completed"
            }
            """
        );

    private sealed class FakeWorkflowDefinitionRepository(string yaml)
        : IWorkflowDefinitionRepository
    {
        public Task<WorkflowDefinitionRecord?> LoadByKeyAndVersionAsync(
            string tenantKey,
            string workflowKey,
            int workflowVersion,
            CancellationToken cancellationToken
        )
            => Task.FromResult<WorkflowDefinitionRecord?>(
                new WorkflowDefinitionRecord(
                    1,
                    workflowKey,
                    workflowVersion,
                    "AI Telegram Test",
                    yaml,
                    "hash",
                    true,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow
                )
            );

        public Task<WorkflowDefinitionRecord?> LoadActiveByKeyAsync(
            string tenantKey,
            string workflowKey,
            CancellationToken cancellationToken
        )
            => LoadByKeyAndVersionAsync(tenantKey, workflowKey, 1, cancellationToken);
    }

    private sealed class FakeWorkflowRunRepository : IWorkflowRunRepository
    {
        private long _nextRunId = 7;
        private readonly Dictionary<string, StepState> _steps = new(StringComparer.Ordinal);
        private JsonElement _triggerPayload;

        public CreatedRunRecord? CreatedRun { get; private set; }
        public WaitingStepRecord? WaitingStep { get; private set; }
        public List<string> CompletedSteps { get; } = [];
        public bool CompletedWorkflow { get; private set; }
        public string? FailedWorkflowReason { get; private set; }

        public Task<long> CreateWorkflowRunAsync(
            string tenantKey,
            string correlationId,
            string workflowKey,
            int workflowVersion,
            JsonElement inputs,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken
        )
        {
            CreatedRun = new CreatedRunRecord(
                tenantKey,
                correlationId,
                workflowKey,
                workflowVersion,
                inputs
            );
            _triggerPayload = inputs.Clone();
            return Task.FromResult(_nextRunId);
        }

        public Task CreateWorkflowStepAsync(
            string tenantKey,
            long workflowRunId,
            string stepKey,
            string taskType,
            WorkflowStepRuntimeStatus status,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken
        )
        {
            _steps[stepKey] = new StepState(stepKey, taskType, status);
            return Task.CompletedTask;
        }

        public Task<WorkflowEventCandidate?> GetWaitingWorkflowByCorrelationAsync(
            string tenantKey,
            string correlationId,
            CancellationToken cancellationToken
        )
        {
            if (CreatedRun is null
                || CreatedRun.TenantKey != tenantKey
                || CreatedRun.CorrelationId != correlationId)
            {
                return Task.FromResult<WorkflowEventCandidate?>(null);
            }

            StepState? step = _steps.Values.LastOrDefault(s =>
                s.Status == WorkflowStepRuntimeStatus.WaitingForEvent);
            if (step is null)
            {
                return Task.FromResult<WorkflowEventCandidate?>(null);
            }

            WorkflowRunContext context = new(
                _nextRunId,
                tenantKey,
                correlationId,
                CreatedRun.WorkflowKey,
                CreatedRun.WorkflowVersion,
                _triggerPayload
            );
            WorkflowStepRuntimeState runtimeStep = new(
                _nextRunId,
                step.StepKey,
                step.TaskType,
                step.Status,
                step.CommandMessageType,
                step.ExpectedCompletedMessageType,
                step.ExpectedFailedMessageType,
                step.CommandMessageId,
                step.Outputs
            );
            return Task.FromResult<WorkflowEventCandidate?>(new WorkflowEventCandidate(context, runtimeStep));
        }

        public Task<IReadOnlyDictionary<string, JsonElement>> GetCompletedStepOutputsAsync(
            string tenantKey,
            long workflowRunId,
            CancellationToken cancellationToken
        )
            => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                _steps.Values
                    .Where(s => s.Status == WorkflowStepRuntimeStatus.Completed && s.Outputs.HasValue)
                    .ToDictionary(s => s.StepKey, s => s.Outputs!.Value, StringComparer.Ordinal)
            );

        public Task<IReadOnlyList<WorkflowEventCandidate>> GetWaitingStepsExpectingEventAsync(
            string tenantKey,
            string messageType,
            CancellationToken cancellationToken
        )
            => Task.FromResult<IReadOnlyList<WorkflowEventCandidate>>(Array.Empty<WorkflowEventCandidate>());

        public Task MarkStepWaitingAsync(
            string tenantKey,
            long workflowRunId,
            string stepKey,
            string commandMessageId,
            string commandMessageType,
            string expectedCompletedMessageType,
            string expectedFailedMessageType,
            JsonElement resolvedInputPayload,
            DateTimeOffset timeoutAtUtc,
            CancellationToken cancellationToken
        )
        {
            StepState step = _steps[stepKey];
            step.Status = WorkflowStepRuntimeStatus.WaitingForEvent;
            step.CommandMessageId = commandMessageId;
            step.CommandMessageType = commandMessageType;
            step.ExpectedCompletedMessageType = expectedCompletedMessageType;
            step.ExpectedFailedMessageType = expectedFailedMessageType;
            step.InputPayload = resolvedInputPayload.Clone();
            WaitingStep = new WaitingStepRecord(
                workflowRunId,
                stepKey,
                commandMessageId,
                commandMessageType,
                expectedCompletedMessageType,
                expectedFailedMessageType,
                resolvedInputPayload.Clone()
            );
            return Task.CompletedTask;
        }

        public Task MarkWorkflowWaitingAsync(
            string tenantKey,
            long workflowRunId,
            string currentStepKey,
            DateTimeOffset updatedAtUtc,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task MarkStepCompletedAsync(
            string tenantKey,
            long workflowRunId,
            string stepKey,
            JsonElement outputs,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken
        )
        {
            StepState step = _steps[stepKey];
            step.Status = WorkflowStepRuntimeStatus.Completed;
            step.Outputs = outputs.Clone();
            CompletedSteps.Add(stepKey);
            return Task.CompletedTask;
        }

        public Task MarkWorkflowCompletedAsync(
            string tenantKey,
            long workflowRunId,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken
        )
        {
            CompletedWorkflow = true;
            return Task.CompletedTask;
        }

        public Task MarkStepFailedAsync(
            string tenantKey,
            long workflowRunId,
            string stepKey,
            string failureReason,
            DateTimeOffset failedAtUtc,
            CancellationToken cancellationToken
        )
        {
            if (_steps.TryGetValue(stepKey, out StepState? step))
            {
                step.Status = WorkflowStepRuntimeStatus.Failed;
            }

            return Task.CompletedTask;
        }

        public Task MarkWorkflowFailedAsync(
            string tenantKey,
            long workflowRunId,
            string failureReason,
            DateTimeOffset failedAtUtc,
            CancellationToken cancellationToken
        )
        {
            FailedWorkflowReason = failureReason;
            return Task.CompletedTask;
        }
    }

    private sealed class StepState(string stepKey, string taskType, WorkflowStepRuntimeStatus status)
    {
        public string StepKey { get; } = stepKey;
        public string TaskType { get; } = taskType;
        public WorkflowStepRuntimeStatus Status { get; set; } = status;
        public string? CommandMessageId { get; set; }
        public string? CommandMessageType { get; set; }
        public string? ExpectedCompletedMessageType { get; set; }
        public string? ExpectedFailedMessageType { get; set; }
        public JsonElement? InputPayload { get; set; }
        public JsonElement? Outputs { get; set; }
    }

    private sealed record CreatedRunRecord(
        string TenantKey,
        string CorrelationId,
        string WorkflowKey,
        int WorkflowVersion,
        JsonElement Inputs
    );

    private sealed record WaitingStepRecord(
        long WorkflowRunId,
        string StepKey,
        string CommandMessageId,
        string CommandMessageType,
        string ExpectedCompletedMessageType,
        string ExpectedFailedMessageType,
        JsonElement InputPayload
    );
}
