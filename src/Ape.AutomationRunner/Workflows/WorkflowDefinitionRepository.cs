using System.Security.Cryptography;
using System.Text;
using Ape.Worker.Sdk.Database;
using MySqlConnector;

namespace Ape.AutomationRunner.Workflows;

public interface IWorkflowDefinitionRepository
{
    Task<IReadOnlyList<WorkflowDefinitionRecord>> ListAsync(
        string tenantKey,
        CancellationToken cancellationToken
    );

    Task<WorkflowDefinitionRecord?> LoadByIdAsync(
        string tenantKey,
        long workflowId,
        CancellationToken cancellationToken
    );

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

    Task<WorkflowDefinitionRecord> CreateAsync(
        string tenantKey,
        string workflowKey,
        int workflowVersion,
        string name,
        string yamlContent,
        bool isActive,
        CancellationToken cancellationToken
    );

    Task<WorkflowDefinitionRecord?> UpdateAsync(
        string tenantKey,
        long workflowId,
        string name,
        string yamlContent,
        bool isActive,
        CancellationToken cancellationToken
    );

    Task<bool> DeactivateAsync(
        string tenantKey,
        long workflowId,
        CancellationToken cancellationToken
    );
}

public sealed class TenantDatabaseNotFoundException(string tenantKey)
    : InvalidOperationException($"No tenant database connection was resolved for tenant {tenantKey}.")
{
    public string TenantKey { get; } = tenantKey;
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
    public async Task<IReadOnlyList<WorkflowDefinitionRecord>> ListAsync(
        string tenantKey,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            SELECT id, workflow_key, workflow_version, name, yaml_content, content_hash,
                   is_active, created_at_utc, updated_at_utc
            FROM workflow_definitions
            ORDER BY workflow_key, workflow_version DESC;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(
            tenantKey,
            cancellationToken
        );
        await using MySqlCommand command = new(sql, connection);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<WorkflowDefinitionRecord> records = new();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    public async Task<WorkflowDefinitionRecord?> LoadByIdAsync(
        string tenantKey,
        long workflowId,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            SELECT id, workflow_key, workflow_version, name, yaml_content, content_hash,
                   is_active, created_at_utc, updated_at_utc
            FROM workflow_definitions
            WHERE id = @workflowId
            LIMIT 1;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(
            tenantKey,
            cancellationToken
        );
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowId", workflowId);

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

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


    public async Task<WorkflowDefinitionRecord> CreateAsync(
        string tenantKey,
        string workflowKey,
        int workflowVersion,
        string name,
        string yamlContent,
        bool isActive,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            INSERT INTO workflow_definitions
                (workflow_key, workflow_version, name, yaml_content, content_hash,
                 is_active, created_at_utc, updated_at_utc)
            VALUES
                (@workflowKey, @workflowVersion, @name, @yamlContent, @contentHash,
                 @isActive, @nowUtc, @nowUtc);
            SELECT LAST_INSERT_ID();
            """;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowKey", workflowKey);
        command.Parameters.AddWithValue("@workflowVersion", workflowVersion);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@yamlContent", yamlContent);
        command.Parameters.AddWithValue("@contentHash", ComputeContentHash(yamlContent));
        command.Parameters.AddWithValue("@isActive", isActive);
        command.Parameters.AddWithValue("@nowUtc", now.UtcDateTime);

        object? id = await command.ExecuteScalarAsync(cancellationToken);
        return await LoadByIdAsync(tenantKey, Convert.ToInt64(id), cancellationToken)
            ?? throw new InvalidOperationException("Created workflow definition could not be loaded.");
    }

    public async Task<WorkflowDefinitionRecord?> UpdateAsync(
        string tenantKey,
        long workflowId,
        string name,
        string yamlContent,
        bool isActive,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            UPDATE workflow_definitions
            SET name = @name,
                yaml_content = @yamlContent,
                content_hash = @contentHash,
                is_active = @isActive,
                updated_at_utc = @updatedAtUtc
            WHERE id = @workflowId;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowId", workflowId);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@yamlContent", yamlContent);
        command.Parameters.AddWithValue("@contentHash", ComputeContentHash(yamlContent));
        command.Parameters.AddWithValue("@isActive", isActive);
        command.Parameters.AddWithValue("@updatedAtUtc", DateTimeOffset.UtcNow.UtcDateTime);

        int affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected == 0
            ? null
            : await LoadByIdAsync(tenantKey, workflowId, cancellationToken);
    }

    public async Task<bool> DeactivateAsync(
        string tenantKey,
        long workflowId,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            UPDATE workflow_definitions
            SET is_active = 0,
                updated_at_utc = @updatedAtUtc
            WHERE id = @workflowId;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowId", workflowId);
        command.Parameters.AddWithValue("@updatedAtUtc", DateTimeOffset.UtcNow.UtcDateTime);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
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
            ?? throw new TenantDatabaseNotFoundException(tenantKey);

        MySqlConnection connection = new(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string ComputeContentHash(string yamlContent)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(yamlContent))).ToLowerInvariant();

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
