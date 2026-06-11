# APE Automation Runner

`Ape.AutomationRunner` is a message-driven workflow coordinator. It consumes `RunWorkflow`, loads YAML workflow definitions from MySQL, executes linear steps, publishes module commands, and resumes waiting steps when correlated completion/failure events are received.

## Runtime modes

The same Docker image can run as either the existing RabbitMQ workflow worker or the workflow management API.

- `APE_SERVICE_MODE=worker` starts the existing message-driven worker and remains the default when the variable is missing or empty.
- `APE_SERVICE_MODE=api` starts the REST API with Swagger UI at `/docs` and a health endpoint at `/health`.
- Unsupported values fail startup with a clear error listing the supported modes.

Workflow API endpoints are tenant-scoped and require the existing `x-ape-tenant-key` header. The API serves workflow CRUD routes from the service root and queues test executions with `POST /{workflowId}/test` by publishing the existing `RunWorkflow` command.

## V1 scope

It **does**:
- Parse/validate workflow YAML with YamlDotNet.
- Persist workflow runs and step state.
- Execute `module.request` by publishing a command and waiting for matching events.
- Preserve correlation IDs across the full workflow run.
- Store small completed-event payloads as step outputs.
- Fail waiting steps via timeout monitor.

It **does not**:
- Call AI directly.
- Call MCP directly.
- Send Telegram directly.
- Implement branching, loops, or parallel step execution.

## Seeded workflow

`send-test-telegram-message` v1 publishes `SendTelegramMessage` and waits for `TelegramMessageSent`/`TelegramMessageFailed`.

## Correlation and resumption

The incoming `RunWorkflow` envelope correlationId is reused for every published step command. Waiting steps are resumed by strict match on `tenantKey + correlationId + expected message type`.

## Timeout monitor

`WorkflowRunnerOptions`:
- `WorkflowRunner__TimeoutMonitorEnabled`
- `WorkflowRunner__TimeoutMonitorIntervalSeconds`
- `WorkflowRunner__DefaultStepTimeoutSeconds`

## Add new workflow

Insert a new row into `workflow_definitions` with unique `(workflow_key, workflow_version)`, mark as active, and provide YAML.

## Sample RunWorkflow payload

```json
{
  "workflowKey": "send-test-telegram-message",
  "workflowVersion": 1,
  "inputs": {}
}
```

## Worker SDK startup migrations

When workers are configured with `AddApeWorkerSdk`, the SDK automatically runs startup migrations before RabbitMQ message consumption begins.

- Set `Migrations__Enabled=true` to run migrations on startup.
- Set `Migrations__Enabled=false` to disable startup migrations.
- Set `ControlApi__BaseUrl`, `ControlApi__TenantDatabasesPath`, and `ControlApi__BearerToken` so the worker can resolve tenant database connection details before applying module migrations.
- Startup fails if migration execution fails, preventing message consumption with an out-of-date schema.
- Future worker services do not need to manually register a migration hosted service when they use `AddApeWorkerSdk`.
