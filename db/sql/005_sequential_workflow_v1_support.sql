SET @column_exists = (
  SELECT COUNT(*)
  FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'workflow_runs'
    AND column_name = 'current_step_key'
);
SET @sql = IF(
  @column_exists = 0,
  'ALTER TABLE workflow_runs ADD COLUMN current_step_key VARCHAR(200) NULL',
  'SELECT 1'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @column_exists = (
  SELECT COUNT(*)
  FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'workflow_runs'
    AND column_name = 'updated_at_utc'
);
SET @sql = IF(
  @column_exists = 0,
  'ALTER TABLE workflow_runs ADD COLUMN updated_at_utc DATETIME NULL',
  'SELECT 1'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

UPDATE workflow_runs
SET updated_at_utc = COALESCE(updated_at_utc, started_at_utc)
WHERE updated_at_utc IS NULL;

SET @column_exists = (
  SELECT COUNT(*)
  FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'workflow_run_steps'
    AND column_name = 'command_message_type'
);
SET @sql = IF(
  @column_exists = 0,
  'ALTER TABLE workflow_run_steps ADD COLUMN command_message_type VARCHAR(200) NULL',
  'SELECT 1'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @column_exists = (
  SELECT COUNT(*)
  FROM information_schema.columns
  WHERE table_schema = DATABASE()
    AND table_name = 'workflow_run_steps'
    AND column_name = 'input_payload_json'
);
SET @sql = IF(
  @column_exists = 0,
  'ALTER TABLE workflow_run_steps ADD COLUMN input_payload_json JSON NULL',
  'SELECT 1'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

INSERT INTO workflow_definitions (workflow_key, workflow_version, name, yaml_content, content_hash, is_active, created_at_utc, updated_at_utc)
SELECT 'ai-telegram-test', 1, 'AI Telegram Test',
'workflowKey: ai-telegram-test\nname: AI Telegram Test\nversion: 1\n\nsteps:\n  - id: generate-message\n    type: command\n    messageType: GenerateTextWithAi\n    payload:\n      systemPrompt: "You are my personal assistant."\n      userPrompt: "Write a short firtly morning greetig to me."\n\n  - id: send-telegram\n    type: command\n    messageType: SendTelegramMessage\n    payload:\n      recipient_id: de755163-2628-495f-9cd9-14ddf163508b\n      text: "{{ steps.generate-message.outputs.generatedText }}"',
'ai-telegram-test-v1', 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()
WHERE NOT EXISTS (
  SELECT 1 FROM workflow_definitions WHERE workflow_key='ai-telegram-test' AND workflow_version=1
);
