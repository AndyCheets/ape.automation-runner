using Ape.Worker.Sdk.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ape.Worker.Sdk.Database;

public sealed class StartupMigrationHostedService : IHostedService
{
    private readonly IDatabaseMigrator _databaseMigrator;
    private readonly IOptions<MigrationOptions> _migrationOptions;
    private readonly ILogger<StartupMigrationHostedService> _logger;

    public StartupMigrationHostedService(
        IDatabaseMigrator databaseMigrator,
        IOptions<MigrationOptions> migrationOptions,
        ILogger<StartupMigrationHostedService> logger)
    {
        _databaseMigrator = databaseMigrator;
        _migrationOptions = migrationOptions;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        MigrationOptions options = _migrationOptions.Value;

        if (!options.Enabled)
        {
            _logger.LogInformation("Database migrations are disabled. Skipping startup migration execution.");
            return;
        }

        _logger.LogInformation(
            "Starting database migrations for module {ModuleKey} using manifest {ManifestPath}.",
            options.ModuleKey,
            options.ManifestPath);

        await _databaseMigrator.MigrateAsync(cancellationToken);

        _logger.LogInformation(
            "Database migrations completed for module {ModuleKey}.",
            options.ModuleKey);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
