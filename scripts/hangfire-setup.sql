-- Hangfire schema setup (guide §14). Run ONCE as a privileged role (postgres / owner),
-- because the app role (rocloud_dev_user) cannot create schemas.
--
--   psql -U postgres -d rocloud_dev -f scripts/hangfire-setup.sql
--
-- After this, the API enables Hangfire automatically and installs its own tables in the
-- "hangfire" schema on startup (it has CREATE on the schema thanks to the grants below).

CREATE SCHEMA IF NOT EXISTS hangfire;

-- Let the application role own/manage objects within the hangfire schema.
GRANT ALL ON SCHEMA hangfire TO rocloud_dev_user;
GRANT ALL ON ALL TABLES    IN SCHEMA hangfire TO rocloud_dev_user;
GRANT ALL ON ALL SEQUENCES IN SCHEMA hangfire TO rocloud_dev_user;

-- Apply the same grants to objects Hangfire creates later.
ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON TABLES    TO rocloud_dev_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON SEQUENCES TO rocloud_dev_user;
