namespace Ape.Worker.Sdk.Database;
public interface IDatabaseMigrator { Task MigrateAsync(CancellationToken cancellationToken); }
