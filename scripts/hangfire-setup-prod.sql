-- ============================================================================
-- Hangfire schema setup — PRODUCTION (rocloud_prod / rocloud_prod_user)
-- ============================================================================
-- Prod counterpart of hangfire-setup.sql (which grants to the DEV role). The
-- production app role rocloud_prod_user has CONNECT but NOT CREATE on the
-- rocloud_prod database, so the API cannot auto-create this schema on startup
-- (it logs "Hangfire is disabled: the 'hangfire' schema is unavailable" and
-- background jobs stay off). Create it once here as a privileged role.
--
-- Run ONCE as the postgres superuser (or the database owner):
--
--     psql -U postgres -d rocloud_prod -f scripts/hangfire-setup-prod.sql
--
-- After this, the API enables Hangfire automatically and installs its own tables
-- in the "hangfire" schema on startup (it has CREATE on the schema via the grants
-- below). Idempotent: safe to re-run.
-- ============================================================================

CREATE SCHEMA IF NOT EXISTS hangfire;

-- Let the production application role own/manage objects within the hangfire schema.
GRANT ALL ON SCHEMA hangfire TO rocloud_prod_user;
GRANT ALL ON ALL TABLES    IN SCHEMA hangfire TO rocloud_prod_user;
GRANT ALL ON ALL SEQUENCES IN SCHEMA hangfire TO rocloud_prod_user;

-- Apply the same grants to objects Hangfire creates later (its tables/sequences).
ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON TABLES    TO rocloud_prod_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON SEQUENCES TO rocloud_prod_user;
