using Microsoft.Extensions.Hosting;

namespace Ape.Worker.Sdk.Messaging;

public sealed class RabbitMqConsumerHostedService : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
