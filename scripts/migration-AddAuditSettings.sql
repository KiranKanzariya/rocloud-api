-- Configurable activity log (guide §10.14): a single global settings row that SuperAdmin edits from
-- the admin portal to control what the audit middleware logs and how long it is retained.
--
-- Platform-level table (no tenant_id, no RLS). Run AS THE postgres SUPERUSER; grant.sql's
-- ALTER DEFAULT PRIVILEGES then grants the app role automatically. Apply to BOTH dev and prod:
--   psql -U postgres -d rocloud_dev  -f migration-AddAuditSettings.sql
--   psql -U postgres -d rocloud_prod -f migration-AddAuditSettings.sql

CREATE TABLE IF NOT EXISTS audit_settings (
    id                       UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    enabled                  BOOLEAN  NOT NULL DEFAULT true,
    capture_request_body     BOOLEAN  NOT NULL DEFAULT true,
    max_request_body_bytes   INTEGER  NOT NULL DEFAULT 102400,
    methods                  TEXT[]   NOT NULL DEFAULT '{POST,PUT,PATCH,DELETE}',
    sensitive_path_prefixes  TEXT[]   NOT NULL DEFAULT '{/api/auth,/api/payments}',
    exclude_modules          TEXT[]   NOT NULL DEFAULT '{}',
    audit_reads_for_modules  TEXT[]   NOT NULL DEFAULT '{}',
    additional_redact_keys   TEXT[]   NOT NULL DEFAULT '{}',
    retention_months         INTEGER  NOT NULL DEFAULT 0,   -- 0 = keep forever
    created_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at               TIMESTAMPTZ,
    updated_by               UUID
);

-- Enforce a single global row (the settings are not per-tenant).
CREATE UNIQUE INDEX IF NOT EXISTS audit_settings_singleton ON audit_settings ((true));

-- Seed the one row with the defaults above (mirrors today's hardcoded behaviour) if none exists.
INSERT INTO audit_settings (id)
SELECT uuid_generate_v4()
WHERE NOT EXISTS (SELECT 1 FROM audit_settings);

-- If your install does NOT use grant.sql's ALTER DEFAULT PRIVILEGES, grant explicitly:
--   GRANT SELECT, INSERT, UPDATE ON audit_settings TO rocloud_dev_user;  -- dev
--   GRANT SELECT, INSERT, UPDATE ON audit_settings TO rocloud_app;       -- prod
