using Ape.Worker.Sdk.Configuration;
using Ape.Worker.Sdk.Database;
using Ape.Worker.Sdk.DependencyInjection;
using Ape.Worker.Sdk.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Ape.Worker.Sdk.Tests;

public sealed class StartupMigrationHostedServiceTests
{
    [Test]
    public async Task StartAsync_MigrationsDisabled_DoesNotRunMigrator()
    {
        RecordingDatabaseMigrator migrator = new();
        StartupMigrationHostedService sut = new(
            migrator,
            Options.Create(new MigrationOptions { Enabled = false, ModuleKey = "sample", ManifestPath = "db/migrations.json" }),
            NullLogger<StartupMigrationHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        Assert.That(migrator.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task StartAsync_MigrationsEnabled_RunsMigrator()
    {
        RecordingDatabaseMigrator migrator = new();
        StartupMigrationHostedService sut = new(
            migrator,
            Options.Create(new MigrationOptions { Enabled = true, ModuleKey = "sample", ManifestPath = "db/migrations.json" }),
            NullLogger<StartupMigrationHostedService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        Assert.That(migrator.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void StartAsync_MigratorThrows_PropagatesException()
    {
        ThrowingDatabaseMigrator migrator = new();
        StartupMigrationHostedService sut = new(
            migrator,
            Options.Create(new MigrationOptions { Enabled = true, ModuleKey = "sample", ManifestPath = "db/migrations.json" }),
            NullLogger<StartupMigrationHostedService>.Instance);

        Assert.That(async () => await sut.StartAsync(CancellationToken.None), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void AddApeWorkerSdk_HostedServicesRegistered_MigrationHostedServiceBeforeRabbitMqConsumer()
    {
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        services.AddApeWorkerSdk(configuration);

        List<Type> hostedServiceTypes = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .Where(type => type is not null)
            .Cast<Type>()
            .ToList();

        int migrationIndex = hostedServiceTypes.IndexOf(typeof(StartupMigrationHostedService));
        int rabbitMqIndex = hostedServiceTypes.IndexOf(typeof(RabbitMqConsumerHostedService));

        Assert.That(migrationIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(rabbitMqIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(migrationIndex, Is.LessThan(rabbitMqIndex));
    }

    private sealed class RecordingDatabaseMigrator : IDatabaseMigrator
    {
        public int CallCount { get; private set; }

        public Task MigrateAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDatabaseMigrator : IDatabaseMigrator
    {
        public Task MigrateAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }
}
