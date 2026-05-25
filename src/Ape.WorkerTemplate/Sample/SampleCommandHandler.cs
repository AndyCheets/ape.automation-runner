using Ape.Worker.Sdk.Messaging;
using Microsoft.Extensions.Logging;
namespace Ape.WorkerTemplate.Sample;
public sealed class SampleCommandHandler(ILogger<SampleCommandHandler> logger) : IMessageHandler
{
    public string MessageType => "SampleCommand";
    public Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    { logger.LogInformation("Received {MessageType}", envelope.MessageType); return Task.CompletedTask; }
}
