-- ============================================================
-- ROCloud — STEP 2: create the application login role
-- Guide §9. Connect to the default "postgres" database as the
-- postgres superuser. Transaction-safe — runs in any client.
--
--   psql:  psql -U postgres -f 02-create-role.sql
--   GUI:   open while connected to the "postgres" database, run all
--
-- Requires step 1 (database rocloud_dev) to have completed.
-- ============================================================

-- Create the application login role only if missing
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'rocloud_dev_user') THEN
        CREATE ROLE rocloud_dev_user LOGIN PASSWORD 'NjQc98y90AGe';
    END IF;
END
$$;

GRANT CONNECT ON DATABASE rocloud_dev TO rocloud_dev_user;

-- ------------------------------------------------------------
-- NEXT: connect to the rocloud_dev database and run, in order:
--   3) schema.sql   (tables, indexes, RLS, seed data)
--   4) grant.sql    (table/sequence privileges for rocloud_dev_user)
--   5) verify.sql   (checks everything applied correctly)
-- ------------------------------------------------------------
