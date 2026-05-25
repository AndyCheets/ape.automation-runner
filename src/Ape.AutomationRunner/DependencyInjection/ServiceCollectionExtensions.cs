using Ape.AutomationRunner.Configuration;
using Ape.AutomationRunner.Timeouts;
using Ape.AutomationRunner.Workflows;
using Ape.AutomationRunner.Workflows.TaskHandlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ape.AutomationRunner.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAutomationRunner(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<WorkflowRunnerOptions>()
            .Bind(configuration.GetSection("WorkflowRunner"));

        services.AddSingleton<WorkflowDefinitionParser>();
        services.AddSingleton<WorkflowDefinitionValidator>();
        services.AddSingleton<WorkflowPayloadTemplateRenderer>();
        services.AddSingleton<WorkflowEventMatcher>();
        services.AddSingleton<IWorkflowRunRepository, NullWorkflowRunRepository>();
        services.AddSingleton<IWorkflowTaskHandler, ModuleRequestTaskHandler>();
        services.AddHostedService<WorkflowTimeoutMonitorHostedService>();

        return services;
    }
}
