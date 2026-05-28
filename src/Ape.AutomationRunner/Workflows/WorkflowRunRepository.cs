using System.Text.Json;
using Ape.AutomationRunner.Workflows.TaskHandlers;
using Ape.Worker.Sdk.Database;
using MySqlConnector;

namespace Ape.AutomationRunner.Workflows;

public sealed class WorkflowRunRepository(
    ITenantDatabaseProvider tenantDatabaseProvider
) : IWorkflowRunRepository
{
    public async Task<long> CreateWorkflowRunAsync(
        string tenantKey,
        string correlationId,
        string workflowKey,
        int workflowVersion,
        JsonElement inputs,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            INSERT INTO workflow_runs
                (workflow_key, workflow_version, tenant_key, correlation_id, status,
                 inputs_json, started_at_utc)
            VALUES
                (@workflowKey, @workflowVersion, @tenantKey, @correlationId, 'Running',
                 @inputsJson, @startedAtUtc);
            SELECT LAST_INSERT_ID();
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(
            tenantKey,
            cancellationToken
        );
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowKey", workflowKey);
        command.Parameters.AddWithValue("@workflowVersion", workflowVersion);
        command.Parameters.AddWithValue("@tenantKey", tenantKey);
        command.Parameters.AddWithValue("@correlationId", correlationId);
        command.Parameters.AddWithValue("@inputsJson", inputs.GetRawText());
        command.Parameters.AddWithValue("@startedAtUtc", startedAtUtc.UtcDateTime);

        object? id = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(id);
    }

    public async Task CreateWorkflowStepAsync(
        string tenantKey,
        long workflowRunId,
        string stepKey,
        string taskType,
        WorkflowStepRuntimeStatus status,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            INSERT INTO workflow_run_steps
                (workflow_run_id, step_key, task_type, status, started_at_utc)
            VALUES
                (@workflowRunId, @stepKey, @taskType, @status, @startedAtUtc);
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(
            tenantKey,
            cancellationToken
        );
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowRunId", workflowRunId);
        command.Parameters.AddWithValue("@stepKey", stepKey);
        command.Parameters.AddWithValue("@taskType", taskType);
        command.Parameters.AddWithValue("@status", status.ToString());
        command.Parameters.AddWithValue("@startedAtUtc", startedAtUtc.UtcDateTime);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowEventCandidate>> GetWaitingStepsExpectingEventAsync(
        string tenantKey,
        string messageType,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            SELECT r.id AS workflow_run_id, r.tenant_key, r.correlation_id, r.workflow_key,
                   r.workflow_version, r.inputs_json, s.step_key, s.task_type, s.status,
                   s.expected_completed_message_type, s.expected_failed_message_type,
                   s.command_message_id
            FROM workflow_run_steps s
            INNER JOIN workflow_runs r ON r.id = s.workflow_run_id
            WHERE r.tenant_key = @tenantKey
              AND s.status = 'Waiting'
              AND (
                    s.expected_completed_message_type = @messageType
                    OR s.expected_failed_message_type = @messageType
                  );
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(
            tenantKey,
            cancellationToken
        );
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@tenantKey", tenantKey);
        command.Parameters.AddWithValue("@messageType", messageType);

        List<WorkflowEventCandidate> candidates = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            JsonElement inputs = JsonSerializer.Deserialize<JsonElement>(
                reader.GetString("inputs_json")
            );
            WorkflowRunContext context = new(
                reader.GetInt64("workflow_run_id"),
                reader.GetString("tenant_key"),
                reader.GetString("correlation_id"),
                reader.GetString("workflow_key"),
                reader.GetInt32("workflow_version"),
                inputs
            );
            WorkflowStepRuntimeState step = new(
                reader.GetInt64("workflow_run_id"),
                reader.GetString("step_key"),
                reader.GetString("task_type"),
                Enum.Parse<WorkflowStepRuntimeStatus>(reader.GetString("status")),
                reader.IsDBNull(reader.GetOrdinal("expected_completed_message_type"))
                    ? null
                    : reader.GetString("expected_completed_message_type"),
                reader.IsDBNull(reader.GetOrdinal("expected_failed_message_type"))
                    ? null
                    : reader.GetString("expected_failed_message_type"),
                reader.IsDBNull(reader.GetOrdinal("command_message_id"))
                    ? null
                    : reader.GetString("command_message_id")
            );
            candidates.Add(new WorkflowEventCandidate(context, step));
        }

        return candidates;
    }

    public async Task MarkStepWaitingAsync(
        string tenantKey,
        long workflowRunId,
        string stepKey,
        string commandMessageId,
        string expectedCompletedMessageType,
        string expectedFailedMessageType,
        DateTimeOffset timeoutAtUtc,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            UPDATE workflow_run_steps
            SET status = 'Waiting',
                command_message_id = @commandMessageId,
                expected_completed_message_type = @expectedCompletedMessageType,
                expected_failed_message_type = @expectedFailedMessageType,
                timeout_at_utc = @timeoutAtUtc
            WHERE workflow_run_id = @workflowRunId AND step_key = @stepKey;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(
            tenantKey,
            cancellationToken
        );
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowRunId", workflowRunId);
        command.Parameters.AddWithValue("@stepKey", stepKey);
        command.Parameters.AddWithValue("@commandMessageId", commandMessageId);
        command.Parameters.AddWithValue("@expectedCompletedMessageType", expectedCompletedMessageType);
        command.Parameters.AddWithValue("@expectedFailedMessageType", expectedFailedMessageType);
        command.Parameters.AddWithValue("@timeoutAtUtc", timeoutAtUtc.UtcDateTime);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkStepFailedAsync(
        string tenantKey,
        long workflowRunId,
        string stepKey,
        string failureReason,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            UPDATE workflow_run_steps
            SET status = 'Failed',
                failed_at_utc = @failedAtUtc,
                failure_reason = @failureReason
            WHERE workflow_run_id = @workflowRunId AND step_key = @stepKey;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(
            tenantKey,
            cancellationToken
        );
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowRunId", workflowRunId);
        command.Parameters.AddWithValue("@stepKey", stepKey);
        command.Parameters.AddWithValue("@failedAtUtc", failedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@failureReason", failureReason);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkWorkflowFailedAsync(
        string tenantKey,
        long workflowRunId,
        string failureReason,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            UPDATE workflow_runs
            SET status = 'Failed',
                failed_at_utc = @failedAtUtc,
                failure_reason = @failureReason
            WHERE id = @workflowRunId;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(
            tenantKey,
            cancellationToken
        );
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowRunId", workflowRunId);
        command.Parameters.AddWithValue("@failedAtUtc", failedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@failureReason", failureReason);

        await command.ExecuteNonQueryAsync(cancellationToken);
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
}
