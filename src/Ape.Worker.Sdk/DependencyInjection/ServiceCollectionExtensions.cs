using Ape.Worker.Sdk.Configuration;
using Ape.Worker.Sdk.Database;
using Ape.Worker.Sdk.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace Ape.Worker.Sdk.DependencyInjection;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApeWorkerSdk(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ServiceIdentityOptions>().Bind(configuration.GetSection("ServiceIdentity"));
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection("RabbitMq"));
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection("Database"));
        services.AddOptions<MigrationOptions>().Bind(configuration.GetSection("Migrations"));
        services.AddSingleton<MessageEnvelopeSerializer>();
        services.AddSingleton<IMessageHandlerRegistry, MessageHandlerRegistry>();
        services.AddSingleton<IMessagePublisher, NullMessagePublisher>();
        services.AddSingleton<IDatabaseMigrator, NullDatabaseMigrator>();
        services.AddHostedService<StartupMigrationHostedService>();
        services.AddHostedService<RabbitMqConsumerHostedService>();
        return services;
    }
    public static IServiceCollection AddMessageHandler<THandler>(this IServiceCollection services) where THandler : class, IMessageHandler
    { services.AddSingleton<IMessageHandler, THandler>(); return services; }
}
