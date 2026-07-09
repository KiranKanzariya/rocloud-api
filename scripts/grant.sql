-- ============================================================
-- ROCloud — STEP 3: grant privileges to the application role
-- Guide §9. Run AFTER schema.sql, connected to the rocloud_dev
-- database, as the postgres superuser. No psql meta-commands.
--
--   psql:  psql -U postgres -d rocloud_dev -f grant.sql
--   GUI:   open while connected to the "rocloud_dev" database, run all
-- ============================================================

GRANT USAGE, CREATE ON SCHEMA public TO rocloud_dev_user;

-- Existing tables/sequences (created by schema.sql)
GRANT ALL PRIVILEGES ON ALL TABLES    IN SCHEMA public TO rocloud_dev_user;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO rocloud_dev_user;

-- Future tables/sequences created by postgres in this schema
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT ALL PRIVILEGES ON TABLES TO rocloud_dev_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT ALL PRIVILEGES ON SEQUENCES TO rocloud_dev_user;
