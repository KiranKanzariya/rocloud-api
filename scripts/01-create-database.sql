-- ============================================================
-- ROCloud — STEP 1: create the database
-- Guide §9. Connect to the default "postgres" database as the
-- postgres superuser.
--
-- IMPORTANT: CREATE DATABASE cannot run inside a transaction block.
-- Many GUI clients (pgAdmin, DBeaver, VS Code PostgreSQL extension)
-- wrap a script in a transaction by default, which causes:
--   ERROR: CREATE DATABASE cannot run inside a transaction block (25001)
--
-- Run this statement ON ITS OWN, in autocommit mode. Easiest options:
--   - psql:  psql -U postgres -c "CREATE DATABASE rocloud_dev;"
--   - GUI:   disable "wrap in transaction" / "autocommit off", then run
--   - GUI:   or use the client's point-and-click "Create Database" feature
--
-- If the database already exists you'll get a harmless
-- "already exists" error — ignore it and move to step 2.
-- ============================================================

CREATE DATABASE rocloud_dev;
