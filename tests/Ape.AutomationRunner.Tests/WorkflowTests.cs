using System.Text.Json;
using Ape.AutomationRunner.Workflows;
using Ape.AutomationRunner.Workflows.TaskHandlers;
using Ape.Worker.Sdk.Messaging;
using Ape.AutomationRunner.Messaging;
using Ape.AutomationRunner.Configuration;
using Ape.Worker.Sdk.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Ape.AutomationRunner.Tests;

public sealed class WorkflowTests
{
    private const string Yaml = """
        workflowKey: send-test-telegram-message
        version: 1
        name: Send Test Telegram Message
        steps:
          - stepKey: send-message
            taskType: module.request
            timeoutSeconds: 120
            config:
              commandMessageType: SendTelegramMessage
              expectedCompletedMessageType: TelegramMessageSent
              expectedFailedMessageType: TelegramMessageFailed
              payload:
                recipient_id: 7f9c6bd4-3c3c-4e1e-9f3d-4ce8b93b8f12
                message: Test
        """;

    private const string SeedYaml = """
        workflowKey: send-test-telegram-message
        version: 1
        name: Send Test Telegram Message
        steps:
          - stepKey: send-message
            taskType: module.request
            timeoutSeconds: 120
            config:
              commandMessageType: SendTelegramMessage
              expectedCompletedMessageType: TelegramMessageSent
              expectedFailedMessageType: TelegramMessageFailed
              payload:
                recipient_id: 7f9c6bd4-3c3c-4e1e-9f3d-4ce8b93b8f12
                message: "Test message from Ape.AutomationRunner"
        """;

    [Test]
    public async Task RunWorkflowCommandHandler_HandleAsync_ValidRunWorkflowCommand_StartsWorkflowExecution()
    {
        Mock<IWorkflowExecutionEngine> engine = new();
        RunWorkflowCommandHandler handler = new(
            engine.Object,
            NullLogger<RunWorkflowCommandHandler>.Instance
        );
        MessageEnvelope envelope = Env(
            "RunWorkflow",
            "tenant",
            "corr",
            """{"workflowKey":"send-test-telegram-message","workflowVersion":1,"inputs":{}}"""
        );

        await handler.HandleAsync(envelope, CancellationToken.None);

        engine.Verify(
            e => e.StartWorkflowAsync(
                envelope,
                It.Is<RunWorkflowCommand>(
                    c => c.WorkflowKey == "send-test-telegram-message"
                        && c.WorkflowVersion == 1
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Test]
    public async Task WorkflowExecutionEngine_StartWorkflowAsync_ValidWorkflow_CreatesWorkflowRun()
    {
        FakeWorkflowDefinitionRepository definitions = new(SeedYaml);
        FakeWorkflowRunRepository runs = new();
        RecordingWorkflowTaskHandler taskHandler = new();
        WorkflowExecutionEngine engine = Engine(definitions, runs, taskHandler);
        MessageEnvelope envelope = Env(
            "RunWorkflow",
            "tenant",
            "corr",
            """{"workflowKey":"send-test-telegram-message","workflowVersion":1,"inputs":{}}"""
        );

        await engine.StartWorkflowAsync(
            envelope,
            new RunWorkflowCommand(
                "send-test-telegram-message",
                1,
                JsonSerializer.Deserialize<JsonElement>("{}")
            ),
            CancellationToken.None
        );

        Assert.That(runs.CreatedRun, Is.Not.Null);
        Assert.That(runs.CreatedRun!.TenantKey, Is.EqualTo("tenant"));
        Assert.That(runs.CreatedRun.CorrelationId, Is.EqualTo("corr"));
    }

    [Test]
    public async Task WorkflowExecutionEngine_StartWorkflowAsync_ValidWorkflow_ExecutesFirstStep()
    {
        FakeWorkflowRunRepository runs = new();
        RecordingWorkflowTaskHandler taskHandler = new();
        WorkflowExecutionEngine engine = Engine(
            new FakeWorkflowDefinitionRepository(SeedYaml),
            runs,
            taskHandler
        );

        await engine.StartWorkflowAsync(
            Env("RunWorkflow", "tenant", "corr"),
            new RunWorkflowCommand(
                "send-test-telegram-message",
                1,
                JsonSerializer.Deserialize<JsonElement>("{}")
            ),
            CancellationToken.None
        );

        Assert.That(taskHandler.HandledStep?.StepKey, Is.EqualTo("send-message"));
        Assert.That(taskHandler.HandledRunContext?.CorrelationId, Is.EqualTo("corr"));
    }

    [Test]
    public void Parse_ValidTelegramYaml_Parses()
    {
        WorkflowDefinitionParser p = new();
        WorkflowDefinition d = p.Parse(Yaml);
        Assert.That(d.WorkflowKey, Is.EqualTo("send-test-telegram-message"));
        Assert.That(d.Steps, Has.Count.EqualTo(1));
        Assert.That(d.Steps[0].StepKey, Is.EqualTo("send-message"));
        Assert.That(d.Steps[0].Config, Is.TypeOf<ModuleRequestWorkflowTaskConfig>());
        ModuleRequestWorkflowTaskConfig config = (ModuleRequestWorkflowTaskConfig)d.Steps[0].Config;
        Assert.That(config.CommandMessageType, Is.EqualTo("SendTelegramMessage"));
        Assert.That(config.Payload.GetProperty("message").GetString(), Is.EqualTo("Test"));
    }

    [Test]
    public void Parse_MultipleSteps_PreservesYamlOrder()
    {
        const string yaml = """
            workflowKey: ordered
            version: 1
            name: Ordered
            steps:
              - stepKey: first
                taskType: module.request
                config:
                  commandMessageType: A
                  expectedCompletedMessageType: ADone
                  expectedFailedMessageType: AFail
                  payload: {}
              - stepKey: second
                taskType: module.request
                config:
                  commandMessageType: B
                  expectedCompletedMessageType: BDone
                  expectedFailedMessageType: BFail
                  payload: {}
            """;

        WorkflowDefinitionParser p = new();
        WorkflowDefinition d = p.Parse(yaml);

        Assert.That(d.Steps.Select(s => s.StepKey), Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void Validate_MissingWorkflowKey_Rejects()
    {
        WorkflowDefinitionValidator v = new();
        WorkflowDefinition d = new("", 1, "n", new List<WorkflowStepDefinition>());
        Assert.That(v.Validate(d), Has.Some.Contains("workflowKey"));
    }

    [Test]
    public void Validate_DuplicateStepKeys_Rejects()
    {
        WorkflowDefinitionValidator v = new();
        JsonElement c = JsonSerializer.Deserialize<JsonElement>("{}");
        WorkflowDefinition d = new(
            "k",
            1,
            "n",
            new[]
            {
                new WorkflowStepDefinition("a", "module.publish", null, new UnknownWorkflowTaskConfig("module.publish", c)),
                new WorkflowStepDefinition("a", "module.publish", null, new UnknownWorkflowTaskConfig("module.publish", c)),
            }
        );
        Assert.That(v.Validate(d), Has.Some.Contains("duplicate"));
    }

    [Test]
    public void Validate_ModuleRequestMissingExpectedCompleted_Rejects()
    {
        WorkflowDefinitionValidator v = new();
        ModuleRequestWorkflowTaskConfig c = new(
            "A",
            string.Empty,
            "B",
            JsonSerializer.Deserialize<JsonElement>("{}")
        );
        WorkflowDefinition d = new(
            "k",
            1,
            "n",
            new[] { new WorkflowStepDefinition("a", "module.request", null, c), }
        );
        Assert.That(v.Validate(d), Has.Some.Contains("expectedCompleted"));
    }

    [Test]
    public void Renderer_WorkflowInputPlaceholder_Resolves()
    {
        WorkflowPayloadTemplateRenderer r = new();
        JsonElement p = JsonSerializer.Deserialize<JsonElement>("""{"x":"{{ workflow.inputs.name }}"}""");
        JsonElement i = JsonSerializer.Deserialize<JsonElement>("""{"name":"john"}""");
        JsonElement o = r.Render(p, i, new Dictionary<string, JsonElement>());
        Assert.That(o.GetProperty("x").GetString(), Is.EqualTo("john"));
    }

    [Test]
    public void Renderer_PreviousStepPlaceholder_Resolves()
    {
        WorkflowPayloadTemplateRenderer r = new();
        JsonElement p = JsonSerializer.Deserialize<JsonElement>("""{"x":"{{ steps.a.outputs.id }}"}""");
        JsonElement i = JsonSerializer.Deserialize<JsonElement>("{}");
        Dictionary<string, JsonElement> outputs = new()
        {
            ["a"] = JsonSerializer.Deserialize<JsonElement>("""{"id":"42"}"""),
        };
        JsonElement o = r.Render(p, i, outputs);
        Assert.That(o.GetProperty("x").GetString(), Is.EqualTo("42"));
    }

    [Test]
    public void Renderer_UnknownPlaceholder_Throws()
    {
        WorkflowPayloadTemplateRenderer r = new();
        JsonElement p = JsonSerializer.Deserialize<JsonElement>("""{"x":"{{ unknown.value }}"}""");
        Assert.Throws<InvalidOperationException>(
            () => r.Render(p, JsonSerializer.Deserialize<JsonElement>("{}"), new Dictionary<string, JsonElement>())
        );
    }

    [Test]
    public void Matcher_CompletedMessage_Matches()
    {
        WorkflowEventMatcher m = new();
        WorkflowStepRuntimeState s = new(1, "a", "module.request", WorkflowStepRuntimeStatus.Waiting, "Done", "Fail", null);
        MessageEnvelope e = Env("Done", "t", "c");
        Assert.That(m.Match(e, s, "t", "c").IsMatch, Is.True);
    }

    [Test]
    public void Matcher_WrongCorrelation_Ignores()
    {
        WorkflowEventMatcher m = new();
        WorkflowStepRuntimeState s = new(1, "a", "module.request", WorkflowStepRuntimeStatus.Waiting, "Done", "Fail", null);
        Assert.That(m.Match(Env("Done", "t", "x"), s, "t", "c").IsMatch, Is.False);
    }

    [Test]
    public void Matcher_WrongTenant_Ignores()
    {
        WorkflowEventMatcher m = new();
        WorkflowStepRuntimeState s = new(1, "a", "module.request", WorkflowStepRuntimeStatus.Waiting, "Done", "Fail", null);
        Assert.That(m.Match(Env("Done", "x", "c"), s, "t", "c").IsMatch, Is.False);
    }

    [Test]
    public void Matcher_FailedMessage_Matches()
    {
        WorkflowEventMatcher m = new();
        WorkflowStepRuntimeState s = new(1, "a", "module.request", WorkflowStepRuntimeStatus.Waiting, "Done", "Fail", null);
        Assert.That(m.Match(Env("Fail", "t", "c"), s, "t", "c").IsFailure, Is.True);
    }

    [Test]
    public async Task ModuleRequest_PreservesCorrelation_AndStoresWaiting()
    {
        Mock<IMessagePublisher> publisher = new();
        Mock<IWorkflowRunRepository> repo = new();
        WorkflowPayloadTemplateRenderer r = new();
        ModuleRequestTaskHandler h = ModuleHandler(publisher.Object, r, repo.Object);

        ModuleRequestWorkflowTaskConfig c = new(
            "SendTelegramMessage",
            "TelegramMessageSent",
            "TelegramMessageFailed",
            JsonSerializer.Deserialize<JsonElement>("""{"message":"x"}""")
        );
        WorkflowRunContext ctx = new(1, "tenant", "corr", "wk", 1, JsonSerializer.Deserialize<JsonElement>("{}"));
        MessageEnvelope cause = Env("RunWorkflow", "tenant", "corr");

        await h.HandleAsync(
            ctx,
            new WorkflowStepDefinition("s", "module.request", 120, c),
            cause,
            new Dictionary<string, JsonElement>(),
            CancellationToken.None
        );

        publisher.Verify(
            p => p.PublishCommandAsync(
                It.Is<MessageEnvelope>(
                    e => e.CorrelationId == "corr" && e.MessageType == "SendTelegramMessage"
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
        repo.Verify(
            rp => rp.MarkStepWaitingAsync(
                "tenant",
                1,
                "s",
                It.IsAny<string>(),
                "TelegramMessageSent",
                "TelegramMessageFailed",
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Test]
    public async Task ModuleRequestTaskHandler_ExecuteAsync_HardCodedTelegramPayload_PublishesSendTelegramMessage()
    {
        Mock<IMessagePublisher> publisher = new();
        FakeWorkflowRunRepository repo = new();
        ModuleRequestTaskHandler handler = ModuleHandler(
            publisher.Object,
            new WorkflowPayloadTemplateRenderer(),
            repo
        );
        WorkflowDefinition definition = new WorkflowDefinitionParser().Parse(SeedYaml);
        WorkflowRunContext context = new(
            7,
            "tenant",
            "corr",
            definition.WorkflowKey,
            definition.Version,
            JsonSerializer.Deserialize<JsonElement>("{}")
        );
        MessageEnvelope cause = Env("RunWorkflow", "tenant", "corr");

        await handler.HandleAsync(
            context,
            definition.Steps[0],
            cause,
            new Dictionary<string, JsonElement>(),
            CancellationToken.None
        );

        publisher.Verify(
            p => p.PublishCommandAsync(
                It.Is<MessageEnvelope>(
                    e => e.MessageType == "SendTelegramMessage"
                        && e.CorrelationId == "corr"
                        && e.TenantKey == "tenant"
                        && e.CausationId == cause.MessageId
                        && e.Payload.GetProperty("recipient_id").GetString() == "7f9c6bd4-3c3c-4e1e-9f3d-4ce8b93b8f12"
                        && e.Payload.GetProperty("message").GetString() == "Test message from Ape.AutomationRunner"
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Test]
    public async Task ModuleRequestTaskHandler_ExecuteAsync_HardCodedTelegramPayload_MarksStepWaiting()
    {
        Mock<IMessagePublisher> publisher = new();
        FakeWorkflowRunRepository repo = new();
        ModuleRequestTaskHandler handler = ModuleHandler(
            publisher.Object,
            new WorkflowPayloadTemplateRenderer(),
            repo
        );
        WorkflowDefinition definition = new WorkflowDefinitionParser().Parse(SeedYaml);
        WorkflowRunContext context = new(
            7,
            "tenant",
            "corr",
            definition.WorkflowKey,
            definition.Version,
            JsonSerializer.Deserialize<JsonElement>("{}")
        );

        await handler.HandleAsync(
            context,
            definition.Steps[0],
            Env("RunWorkflow", "tenant", "corr"),
            new Dictionary<string, JsonElement>(),
            CancellationToken.None
        );

        Assert.That(repo.WaitingStep, Is.Not.Null);
        Assert.That(repo.WaitingStep!.WorkflowRunId, Is.EqualTo(7));
        Assert.That(repo.WaitingStep.StepKey, Is.EqualTo("send-message"));
        Assert.That(repo.WaitingStep.CommandMessageId, Is.Not.Empty);
        Assert.That(repo.WaitingStep.ExpectedCompletedMessageType, Is.EqualTo("TelegramMessageSent"));
        Assert.That(repo.WaitingStep.ExpectedFailedMessageType, Is.EqualTo("TelegramMessageFailed"));
    }

    [Test]
    public async Task WorkflowDefinitionRepository_LoadByKeyAndVersion_Found_ReturnsYamlContent()
    {
        FakeWorkflowDefinitionRepository repository = new(SeedYaml);

        WorkflowDefinitionRecord? record = await repository.LoadByKeyAndVersionAsync(
            "tenant",
            "send-test-telegram-message",
            1,
            CancellationToken.None
        );

        Assert.That(record, Is.Not.Null);
        Assert.That(record!.YamlContent, Is.EqualTo(SeedYaml));
    }

    [Test]
    public void WorkflowDefinitionParser_ParseSimpleTelegramWorkflow_ReturnsExpectedStep()
    {
        WorkflowDefinition definition = new WorkflowDefinitionParser().Parse(SeedYaml);

        Assert.That(definition.WorkflowKey, Is.EqualTo("send-test-telegram-message"));
        Assert.That(definition.Version, Is.EqualTo(1));
        Assert.That(definition.Steps, Has.Count.EqualTo(1));
        Assert.That(definition.Steps[0].StepKey, Is.EqualTo("send-message"));
        Assert.That(definition.Steps[0].TaskType, Is.EqualTo("module.request"));
        ModuleRequestWorkflowTaskConfig config =
            (ModuleRequestWorkflowTaskConfig)definition.Steps[0].Config;
        Assert.That(config.CommandMessageType, Is.EqualTo("SendTelegramMessage"));
        Assert.That(
            config.Payload.GetProperty("recipient_id").GetString(),
            Is.EqualTo("7f9c6bd4-3c3c-4e1e-9f3d-4ce8b93b8f12")
        );
    }

    [Test]
    public async Task ProgressHandler_NoInProgressRuns_IgnoresEvent()
    {
        Mock<IWorkflowRunRepository> repo = new();
        repo.Setup(
                r => r.GetWaitingStepsExpectingEventAsync(
                    "tenant",
                    "TelegramMessageSent",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Array.Empty<WorkflowEventCandidate>());

        WorkflowProgressEventHandler handler = new(
            repo.Object,
            new WorkflowEventMatcher(),
            NullLogger<WorkflowProgressEventHandler>.Instance
        );

        await handler.HandleAsync(Env("TelegramMessageSent", "tenant", "corr"), CancellationToken.None);

        repo.Verify(
            r => r.GetWaitingStepsExpectingEventAsync(
                "tenant",
                "TelegramMessageSent",
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Test]
    public async Task ProgressHandler_QueryFiltersByExpectedEvent()
    {
        Mock<IWorkflowRunRepository> repo = new();
        repo.Setup(
                r => r.GetWaitingStepsExpectingEventAsync(
                    "tenant",
                    "TelegramMessageSent",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new[] { Candidate("tenant", "corr", "TelegramMessageSent", "TelegramMessageFailed") });

        WorkflowProgressEventHandler handler = new(
            repo.Object,
            new WorkflowEventMatcher(),
            NullLogger<WorkflowProgressEventHandler>.Instance
        );

        await handler.HandleAsync(Env("TelegramMessageSent", "tenant", "corr"), CancellationToken.None);

        repo.Verify(
            r => r.GetWaitingStepsExpectingEventAsync(
                "tenant",
                "TelegramMessageSent",
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Test]
    public void ProgressHandler_ExpectedEventWithWrongCorrelation_DoesNotThrow()
    {
        Mock<IWorkflowRunRepository> repo = new();
        repo.Setup(
                r => r.GetWaitingStepsExpectingEventAsync(
                    "tenant",
                    "TelegramMessageSent",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new[] { Candidate("tenant", "other-corr", "TelegramMessageSent", "TelegramMessageFailed") });

        WorkflowProgressEventHandler handler = new(
            repo.Object,
            new WorkflowEventMatcher(),
            NullLogger<WorkflowProgressEventHandler>.Instance
        );

        Assert.DoesNotThrowAsync(
            () => handler.HandleAsync(Env("TelegramMessageSent", "tenant", "corr"), CancellationToken.None)
        );
    }

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
            JsonSerializer.Deserialize<JsonElement>(payload)
        );

    private static ModuleRequestTaskHandler ModuleHandler(
        IMessagePublisher publisher,
        WorkflowPayloadTemplateRenderer renderer,
        IWorkflowRunRepository repository
    )
        => new(
            publisher,
            renderer,
            repository,
            Options.Create(new WorkflowRunnerOptions()),
            Options.Create(new ServiceIdentityOptions { Source = "ape.automation-runner" }),
            NullLogger<ModuleRequestTaskHandler>.Instance
        );

    private static WorkflowExecutionEngine Engine(
        IWorkflowDefinitionRepository definitions,
        IWorkflowRunRepository runs,
        IWorkflowTaskHandler taskHandler
    )
        => new(
            definitions,
            new WorkflowDefinitionParser(),
            new WorkflowDefinitionValidator(),
            runs,
            new WorkflowTaskHandlerRegistry(new[] { taskHandler }),
            NullLogger<WorkflowExecutionEngine>.Instance
        );

    private static WorkflowEventCandidate Candidate(
        string tenant,
        string correlation,
        string completedMessageType,
        string failedMessageType
    )
    {
        WorkflowRunContext context = new(
            1,
            tenant,
            correlation,
            "workflow",
            1,
            JsonSerializer.Deserialize<JsonElement>("{}")
        );
        WorkflowStepRuntimeState step = new(
            1,
            "step",
            "module.request",
            WorkflowStepRuntimeStatus.Waiting,
            completedMessageType,
            failedMessageType,
            null
        );
        return new WorkflowEventCandidate(context, step);
    }

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
                    "Send Test Telegram Message",
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
        public CreatedRunRecord? CreatedRun { get; private set; }
        public WaitingStepRecord? WaitingStep { get; private set; }

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
            return Task.FromResult(7L);
        }

        public Task CreateWorkflowStepAsync(
            string tenantKey,
            long workflowRunId,
            string stepKey,
            string taskType,
            WorkflowStepRuntimeStatus status,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task<IReadOnlyList<WorkflowEventCandidate>> GetWaitingStepsExpectingEventAsync(
            string tenantKey,
            string messageType,
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<WorkflowEventCandidate>>(Array.Empty<WorkflowEventCandidate>());

        public Task MarkStepWaitingAsync(
            string tenantKey,
            long workflowRunId,
            string stepKey,
            string commandMessageId,
            string expectedCompletedMessageType,
            string expectedFailedMessageType,
            DateTimeOffset timeoutAtUtc,
            CancellationToken cancellationToken
        )
        {
            WaitingStep = new WaitingStepRecord(
                workflowRunId,
                stepKey,
                commandMessageId,
                expectedCompletedMessageType,
                expectedFailedMessageType
            );
            return Task.CompletedTask;
        }

        public Task MarkStepFailedAsync(
            string tenantKey,
            long workflowRunId,
            string stepKey,
            string failureReason,
            DateTimeOffset failedAtUtc,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task MarkWorkflowFailedAsync(
            string tenantKey,
            long workflowRunId,
            string failureReason,
            DateTimeOffset failedAtUtc,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    private sealed class RecordingWorkflowTaskHandler : IWorkflowTaskHandler
    {
        public string TaskType => "module.request";
        public WorkflowRunContext? HandledRunContext { get; private set; }
        public WorkflowStepDefinition? HandledStep { get; private set; }

        public Task<WorkflowStepRuntimeStatus> HandleAsync(
            WorkflowRunContext runContext,
            WorkflowStepDefinition step,
            MessageEnvelope causeEnvelope,
            IReadOnlyDictionary<string, JsonElement> stepOutputs,
            CancellationToken cancellationToken
        )
        {
            HandledRunContext = runContext;
            HandledStep = step;
            return Task.FromResult(WorkflowStepRuntimeStatus.Waiting);
        }
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
        string ExpectedCompletedMessageType,
        string ExpectedFailedMessageType
    );
}
