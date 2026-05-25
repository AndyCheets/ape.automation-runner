using Ape.Worker.Sdk.DependencyInjection;
using Ape.WorkerTemplate.Sample;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddApeWorkerSdk(builder.Configuration);
builder.Services.AddMessageHandler<SampleCommandHandler>();
IHost host = builder.Build();
await host.RunAsync();
