CREATE TABLE IF NOT EXISTS workflow_run_steps (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  workflow_run_id BIGINT NOT NULL,
  step_key VARCHAR(200) NOT NULL,
  task_type VARCHAR(100) NOT NULL,
  status VARCHAR(50) NOT NULL,
  command_message_id VARCHAR(200) NULL,
  expected_completed_message_type VARCHAR(200) NULL,
  expected_failed_message_type VARCHAR(200) NULL,
  outputs_json JSON NULL,
  started_at_utc DATETIME NOT NULL,
  completed_at_utc DATETIME NULL,
  failed_at_utc DATETIME NULL,
  timeout_at_utc DATETIME NULL,
  failure_reason TEXT NULL,
  CONSTRAINT fk_workflow_steps_run FOREIGN KEY (workflow_run_id) REFERENCES workflow_runs(id)
);
