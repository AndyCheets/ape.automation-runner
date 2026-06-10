using Ape.AutomationRunner.Api;
using Ape.AutomationRunner.Api.Services;
using Ape.AutomationRunner.DependencyInjection;
using Ape.AutomationRunner.Messaging;
using Ape.AutomationRunner.Runtime;
using Ape.Worker.Sdk.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

ApeServiceMode mode;
try
{
    mode = ApeServiceModeResolver.Resolve(
        Environment.GetEnvironmentVariable(ApeServiceModeResolver.EnvironmentVariableName)
    );
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Automation runner startup failed: {ex.Message}");
    throw;
}

if (mode == ApeServiceMode.Api)
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Services.AddApeWorkerSdk(builder.Configuration);
    builder.Services.AddAutomationRunner(builder.Configuration, includeHostedServices: false);
    builder.Services.AddScoped<IWorkflowApiService, WorkflowApiService>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "APE Automation Runner Workflow API",
            Version = "v1",
            Description = "Tenant-scoped workflow definition management and asynchronous workflow test execution."
        });
    });

    WebApplication app = builder.Build();
    app.Logger.LogInformation("Starting Ape.AutomationRunner in API mode.");
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "docs";
    });
    app.MapAutomationRunnerApi();

    await app.RunAsync();
}
else
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddApeWorkerSdk(builder.Configuration);
    builder.Services.AddAutomationRunner(builder.Configuration);
    builder.Services.AddMessageHandler<RunWorkflowCommandHandler>();
    builder.Services.AddMessageHandler<WorkflowProgressEventHandler>();

    IHost host = builder.Build();
    ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Starting Ape.AutomationRunner in worker mode.");
    await host.RunAsync();
}
