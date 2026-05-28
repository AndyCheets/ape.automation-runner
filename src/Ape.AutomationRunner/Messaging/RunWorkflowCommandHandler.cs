using System.Text.Json;
using Ape.Worker.Sdk.Messaging;
using Ape.AutomationRunner.Workflows;
using Microsoft.Extensions.Logging;

namespace Ape.AutomationRunner.Messaging;

public sealed class RunWorkflowCommandHandler(
    IWorkflowExecutionEngine workflowExecutionEngine,
    ILogger<RunWorkflowCommandHandler> logger
) : IMessageHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string MessageType => "RunWorkflow";

    public async Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "RunWorkflow received for {TenantKey} correlation {CorrelationId}",
            envelope.TenantKey,
            envelope.CorrelationId
        );

        RunWorkflowCommand? command;
        try
        {
            command = envelope.Payload.Deserialize<RunWorkflowCommand>(SerializerOptions);
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "RunWorkflow payload could not be deserialized for {TenantKey} correlation {CorrelationId}",
                envelope.TenantKey,
                envelope.CorrelationId
            );
            return;
        }

        if (command is null)
        {
            logger.LogError(
                "RunWorkflow payload was empty for {TenantKey} correlation {CorrelationId}",
                envelope.TenantKey,
                envelope.CorrelationId
            );
            return;
        }

        await workflowExecutionEngine.StartWorkflowAsync(envelope, command, cancellationToken);
    }
}
