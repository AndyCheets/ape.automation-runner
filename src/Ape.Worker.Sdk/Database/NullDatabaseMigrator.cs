namespace Ape.Worker.Sdk.Database;

public sealed class NullDatabaseMigrator : IDatabaseMigrator
{
    public Task MigrateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
