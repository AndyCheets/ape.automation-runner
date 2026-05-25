CREATE TABLE IF NOT EXISTS workflow_runs (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  workflow_key VARCHAR(200) NOT NULL,
  workflow_version INT NOT NULL,
  tenant_key VARCHAR(200) NOT NULL,
  correlation_id VARCHAR(200) NOT NULL,
  status VARCHAR(50) NOT NULL,
  inputs_json JSON NULL,
  started_at_utc DATETIME NOT NULL,
  completed_at_utc DATETIME NULL,
  failed_at_utc DATETIME NULL,
  failure_reason TEXT NULL
);
