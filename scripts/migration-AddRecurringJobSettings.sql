-- Per-recurring-job overrides (cron schedule + enabled flag) for the super-admin portal's
-- Background Jobs page. App-owned, NOT tenant-scoped (no RLS). The code (RecurringJobRegistration)
-- seeds a row per job on first startup if missing, then honours these values on every startup.
--
-- Run AS THE DATABASE OWNER (postgres). grant.sql's ALTER DEFAULT PRIVILEGES then grants the app
-- role automatically. Apply to BOTH dev and prod.
--   psql:  psql -U postgres -d rocloud_dev -f migration-AddRecurringJobSettings.sql
--
-- If your install does NOT use grant.sql's ALTER DEFAULT PRIVILEGES, grant explicitly:
--   GRANT SELECT, INSERT, UPDATE, DELETE ON recurring_job_settings TO rocloud_dev_user;

CREATE TABLE IF NOT EXISTS recurring_job_settings (
    job_id     VARCHAR(100) PRIMARY KEY,
    cron       VARCHAR(120) NOT NULL,
    enabled    BOOLEAN      NOT NULL DEFAULT TRUE,
    updated_at TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
