using Ape.Worker.Sdk.Database;
using MySqlConnector;

namespace Ape.AutomationRunner.Workflows;

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinitionRecord?> LoadByKeyAndVersionAsync(
        string tenantKey,
        string workflowKey,
        int workflowVersion,
        CancellationToken cancellationToken
    );

    Task<WorkflowDefinitionRecord?> LoadActiveByKeyAsync(
        string tenantKey,
        string workflowKey,
        CancellationToken cancellationToken
    );
}

public sealed record WorkflowDefinitionRecord(
    long Id,
    string WorkflowKey,
    int WorkflowVersion,
    string Name,
    string YamlContent,
    string ContentHash,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
);

public sealed class WorkflowDefinitionRepository(
    ITenantDatabaseProvider tenantDatabaseProvider
) : IWorkflowDefinitionRepository
{
    public async Task<WorkflowDefinitionRecord?> LoadByKeyAndVersionAsync(
        string tenantKey,
        string workflowKey,
        int workflowVersion,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            SELECT id, workflow_key, workflow_version, name, yaml_content, content_hash,
                   is_active, created_at_utc, updated_at_utc
            FROM workflow_definitions
            WHERE workflow_key = @workflowKey AND workflow_version = @workflowVersion
            LIMIT 1;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(
            tenantKey,
            cancellationToken
        );
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowKey", workflowKey);
        command.Parameters.AddWithValue("@workflowVersion", workflowVersion);

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    public async Task<WorkflowDefinitionRecord?> LoadActiveByKeyAsync(
        string tenantKey,
        string workflowKey,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            SELECT id, workflow_key, workflow_version, name, yaml_content, content_hash,
                   is_active, created_at_utc, updated_at_utc
            FROM workflow_definitions
            WHERE workflow_key = @workflowKey AND is_active = 1
            ORDER BY workflow_version DESC
            LIMIT 1;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(
            tenantKey,
            cancellationToken
        );
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowKey", workflowKey);

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    private async Task<MySqlConnection> OpenTenantConnectionAsync(
        string tenantKey,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<TenantDatabaseInfo> databases =
            await tenantDatabaseProvider.GetTenantDatabasesAsync(cancellationToken);
        TenantDatabaseInfo database = databases.FirstOrDefault(
                d => string.Equals(d.TenantKey, tenantKey, StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new InvalidOperationException(
                $"No tenant database connection was resolved for tenant {tenantKey}."
            );

        MySqlConnection connection = new(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static WorkflowDefinitionRecord ReadRecord(MySqlDataReader reader)
        => new(
            reader.GetInt64("id"),
            reader.GetString("workflow_key"),
            reader.GetInt32("workflow_version"),
            reader.GetString("name"),
            reader.GetString("yaml_content"),
            reader.GetString("content_hash"),
            reader.GetBoolean("is_active"),
            new DateTimeOffset(reader.GetDateTime("created_at_utc"), TimeSpan.Zero),
            new DateTimeOffset(reader.GetDateTime("updated_at_utc"), TimeSpan.Zero)
        );
}
