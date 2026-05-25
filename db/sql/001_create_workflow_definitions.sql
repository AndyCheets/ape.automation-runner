CREATE TABLE IF NOT EXISTS workflow_definitions (
  id BIGINT PRIMARY KEY AUTO_INCREMENT,
  workflow_key VARCHAR(200) NOT NULL,
  workflow_version INT NOT NULL,
  name VARCHAR(255) NOT NULL,
  yaml_content TEXT NOT NULL,
  content_hash VARCHAR(128) NOT NULL,
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  created_at_utc DATETIME NOT NULL,
  updated_at_utc DATETIME NOT NULL,
  UNIQUE KEY uq_workflow_key_version (workflow_key, workflow_version)
);
