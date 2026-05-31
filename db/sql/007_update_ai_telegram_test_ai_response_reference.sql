UPDATE workflow_definitions
SET yaml_content = 'workflowKey: ai-telegram-test\nname: AI Telegram Test\nversion: 1\n\nsteps:\n  - id: generate-message\n    type: command\n    messageType: GenerateTextWithAi\n    payload:\n      systemPrompt: "You are my personal assistant."\n      userPrompt: "Write a short firtly morning greetig to me."\n\n  - id: send-telegram\n    type: command\n    messageType: SendTelegramMessage\n    payload:\n      recipient_id: "{{ trigger.payload.recipient_id }}"\n      contentSource:\n        type: ai-response\n        id: "{{ steps.generate-message.outputs.aiResponseId }}"',
    content_hash = 'ai-telegram-test-v1-ai-response-reference',
    updated_at_utc = UTC_TIMESTAMP()
WHERE workflow_key = 'ai-telegram-test'
  AND workflow_version = 1;
