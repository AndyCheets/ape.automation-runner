using Ape.AutomationRunner.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace Ape.AutomationRunner.Timeouts;
public sealed class WorkflowTimeoutMonitorHostedService(ILogger<WorkflowTimeoutMonitorHostedService> logger, IOptions<WorkflowRunnerOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.TimeoutMonitorEnabled) { return; }
        while (!stoppingToken.IsCancellationRequested)
        { logger.LogDebug("Workflow timeout monitor tick"); await Task.Delay(TimeSpan.FromSeconds(options.Value.TimeoutMonitorIntervalSeconds), stoppingToken); }
    }
}
