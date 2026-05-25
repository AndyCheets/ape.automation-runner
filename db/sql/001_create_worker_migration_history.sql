CREATE TABLE IF NOT EXISTS ape_module_migration_history (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  module_key VARCHAR(128) NOT NULL,
  migration_id VARCHAR(255) NOT NULL,
  checksum VARCHAR(128) NOT NULL,
  applied_at_utc DATETIME(6) NOT NULL,
  UNIQUE KEY uk_mod_mig (module_key, migration_id)
);
