using Ape.AutomationRunner.DependencyInjection;
using Ape.AutomationRunner.Messaging;
using Ape.Worker.Sdk.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApeWorkerSdk(builder.Configuration);
builder.Services.AddAutomationRunner(builder.Configuration);
builder.Services.AddMessageHandler<RunWorkflowCommandHandler>();
builder.Services.AddMessageHandler<WorkflowProgressEventHandler>();

IHost host = builder.Build();
await host.RunAsync();
