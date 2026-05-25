INSERT INTO workflow_definitions (workflow_key, workflow_version, name, yaml_content, content_hash, is_active, created_at_utc, updated_at_utc)
SELECT 'send-test-telegram-message', 1, 'Send Test Telegram Message',
'workflowKey: send-test-telegram-message\nversion: 1\nname: Send Test Telegram Message\nsteps:\n  - stepKey: send-message\n    taskType: module.request\n    timeoutSeconds: 120\n    config:\n      commandMessageType: SendTelegramMessage\n      expectedCompletedMessageType: TelegramMessageSent\n      expectedFailedMessageType: TelegramMessageFailed\n      payload:\n        destinationKey: bite-main-telegram\n        message: "Test message from Ape.AutomationRunner"',
'send-test-telegram-message-v1',1,UTC_TIMESTAMP(),UTC_TIMESTAMP()
WHERE NOT EXISTS (
  SELECT 1 FROM workflow_definitions WHERE workflow_key='send-test-telegram-message' AND workflow_version=1
);
