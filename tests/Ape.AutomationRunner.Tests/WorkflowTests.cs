using System.Text.Json;
using Ape.AutomationRunner.Workflows;
using Ape.AutomationRunner.Workflows.TaskHandlers;
using Ape.Worker.Sdk.Messaging;
using Ape.AutomationRunner.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
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
                destinationKey: bite-main-telegram
                message: Test
        """;

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
        ModuleRequestTaskHandler h = new(publisher.Object, r, repo.Object);

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

    private static MessageEnvelope Env(string type, string tenant, string correlation)
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
            JsonSerializer.Deserialize<JsonElement>("{}")
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
}
