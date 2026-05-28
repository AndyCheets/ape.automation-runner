CREATE TABLE IF NOT EXISTS module_schema_migrations (
  id BIGINT NOT NULL AUTO_INCREMENT,
  module_name VARCHAR(100) COLLATE utf8mb4_unicode_ci NOT NULL,
  script_name VARCHAR(255) COLLATE utf8mb4_unicode_ci NOT NULL,
  script_checksum CHAR(64) COLLATE utf8mb4_unicode_ci NOT NULL,
  executed_at_utc DATETIME NOT NULL,
  execution_time_ms INT DEFAULT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_module_schema_migrations_module_script (module_name, script_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
