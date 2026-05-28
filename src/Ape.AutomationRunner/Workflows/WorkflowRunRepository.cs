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
                 inputs_json, started_at_utc, updated_at_utc)
            VALUES
                (@workflowKey, @workflowVersion, @tenantKey, @correlationId, 'Running',
                 @inputsJson, @startedAtUtc, @startedAtUtc);
            SELECT LAST_INSERT_ID();
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
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

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowRunId", workflowRunId);
        command.Parameters.AddWithValue("@stepKey", stepKey);
        command.Parameters.AddWithValue("@taskType", taskType);
        command.Parameters.AddWithValue("@status", status.ToString());
        command.Parameters.AddWithValue("@startedAtUtc", startedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WorkflowEventCandidate?> GetWaitingWorkflowByCorrelationAsync(
        string tenantKey,
        string correlationId,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            SELECT r.id AS workflow_run_id, r.tenant_key, r.correlation_id, r.workflow_key,
                   r.workflow_version, r.inputs_json, s.step_key, s.task_type, s.status,
                   s.command_message_type, s.expected_completed_message_type,
                   s.expected_failed_message_type, s.command_message_id, s.outputs_json
            FROM workflow_runs r
            INNER JOIN workflow_run_steps s ON s.workflow_run_id = r.id
            WHERE r.tenant_key = @tenantKey
              AND r.correlation_id = @correlationId
              AND r.status = 'WaitingForEvent'
              AND s.status = 'WaitingForEvent'
            ORDER BY s.id DESC
            LIMIT 1;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@tenantKey", tenantKey);
        command.Parameters.AddWithValue("@correlationId", correlationId);

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCandidate(reader) : null;
    }

    public async Task<IReadOnlyDictionary<string, JsonElement>> GetCompletedStepOutputsAsync(
        string tenantKey,
        long workflowRunId,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            SELECT step_key, outputs_json
            FROM workflow_run_steps
            WHERE workflow_run_id = @workflowRunId
              AND status = 'Completed'
              AND outputs_json IS NOT NULL
            ORDER BY id ASC;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowRunId", workflowRunId);

        Dictionary<string, JsonElement> outputs = new(StringComparer.Ordinal);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            outputs[reader.GetString("step_key")] = JsonSerializer.Deserialize<JsonElement>(
                reader.GetString("outputs_json")
            );
        }

        return outputs;
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
                   s.command_message_type, s.expected_completed_message_type,
                   s.expected_failed_message_type, s.command_message_id, s.outputs_json
            FROM workflow_run_steps s
            INNER JOIN workflow_runs r ON r.id = s.workflow_run_id
            WHERE r.tenant_key = @tenantKey
              AND r.status = 'WaitingForEvent'
              AND s.status = 'WaitingForEvent'
              AND (
                    s.expected_completed_message_type = @messageType
                    OR s.expected_failed_message_type = @messageType
                  );
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@tenantKey", tenantKey);
        command.Parameters.AddWithValue("@messageType", messageType);

        List<WorkflowEventCandidate> candidates = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(ReadCandidate(reader));
        }

        return candidates;
    }

    public async Task MarkStepWaitingAsync(
        string tenantKey,
        long workflowRunId,
        string stepKey,
        string commandMessageId,
        string commandMessageType,
        string expectedCompletedMessageType,
        string expectedFailedMessageType,
        JsonElement resolvedInputPayload,
        DateTimeOffset timeoutAtUtc,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            UPDATE workflow_run_steps
            SET status = 'WaitingForEvent',
                command_message_id = @commandMessageId,
                command_message_type = @commandMessageType,
                expected_completed_message_type = @expectedCompletedMessageType,
                expected_failed_message_type = @expectedFailedMessageType,
                input_payload_json = @inputPayloadJson,
                timeout_at_utc = @timeoutAtUtc
            WHERE workflow_run_id = @workflowRunId AND step_key = @stepKey;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowRunId", workflowRunId);
        command.Parameters.AddWithValue("@stepKey", stepKey);
        command.Parameters.AddWithValue("@commandMessageId", commandMessageId);
        command.Parameters.AddWithValue("@commandMessageType", commandMessageType);
        command.Parameters.AddWithValue("@expectedCompletedMessageType", expectedCompletedMessageType);
        command.Parameters.AddWithValue("@expectedFailedMessageType", expectedFailedMessageType);
        command.Parameters.AddWithValue("@inputPayloadJson", resolvedInputPayload.GetRawText());
        command.Parameters.AddWithValue("@timeoutAtUtc", timeoutAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkWorkflowWaitingAsync(
        string tenantKey,
        long workflowRunId,
        string currentStepKey,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            UPDATE workflow_runs
            SET status = 'WaitingForEvent',
                current_step_key = @currentStepKey,
                updated_at_utc = @updatedAtUtc
            WHERE id = @workflowRunId;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowRunId", workflowRunId);
        command.Parameters.AddWithValue("@currentStepKey", currentStepKey);
        command.Parameters.AddWithValue("@updatedAtUtc", updatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkStepCompletedAsync(
        string tenantKey,
        long workflowRunId,
        string stepKey,
        JsonElement outputs,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            UPDATE workflow_run_steps
            SET status = 'Completed',
                outputs_json = @outputsJson,
                completed_at_utc = @completedAtUtc
            WHERE workflow_run_id = @workflowRunId AND step_key = @stepKey;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowRunId", workflowRunId);
        command.Parameters.AddWithValue("@stepKey", stepKey);
        command.Parameters.AddWithValue("@outputsJson", outputs.GetRawText());
        command.Parameters.AddWithValue("@completedAtUtc", completedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkWorkflowCompletedAsync(
        string tenantKey,
        long workflowRunId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            UPDATE workflow_runs
            SET status = 'Completed',
                current_step_key = NULL,
                completed_at_utc = @completedAtUtc,
                updated_at_utc = @completedAtUtc
            WHERE id = @workflowRunId;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@workflowRunId", workflowRunId);
        command.Parameters.AddWithValue("@completedAtUtc", completedAtUtc.UtcDateTime);
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

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
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
                current_step_key = NULL,
                failed_at_utc = @failedAtUtc,
                failure_reason = @failureReason,
                updated_at_utc = @failedAtUtc
            WHERE id = @workflowRunId;
            """;

        await using MySqlConnection connection = await OpenTenantConnectionAsync(tenantKey, cancellationToken);
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

    private static WorkflowEventCandidate ReadCandidate(MySqlDataReader reader)
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
        JsonElement? outputs = reader.IsDBNull(reader.GetOrdinal("outputs_json"))
            ? null
            : JsonSerializer.Deserialize<JsonElement>(reader.GetString("outputs_json"));
        WorkflowStepRuntimeState step = new(
            reader.GetInt64("workflow_run_id"),
            reader.GetString("step_key"),
            reader.GetString("task_type"),
            Enum.Parse<WorkflowStepRuntimeStatus>(reader.GetString("status")),
            reader.IsDBNull(reader.GetOrdinal("command_message_type"))
                ? null
                : reader.GetString("command_message_type"),
            reader.IsDBNull(reader.GetOrdinal("expected_completed_message_type"))
                ? null
                : reader.GetString("expected_completed_message_type"),
            reader.IsDBNull(reader.GetOrdinal("expected_failed_message_type"))
                ? null
                : reader.GetString("expected_failed_message_type"),
            reader.IsDBNull(reader.GetOrdinal("command_message_id"))
                ? null
                : reader.GetString("command_message_id"),
            outputs
        );

        return new WorkflowEventCandidate(context, step);
    }
}
