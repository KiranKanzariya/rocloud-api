-- Make audit_logs tamper-evident / append-only (guide §10.14, Phase 15).
-- Run as a privileged role (postgres / table owner):
--
--   psql -U postgres -d rocloud_dev -f scripts/audit-permissions.sql
--
-- The application role may INSERT and SELECT, but never UPDATE or DELETE — so audit history
-- cannot be altered after the fact. Applies to the partitioned parent and all partitions.

REVOKE UPDATE, DELETE, TRUNCATE ON audit_logs FROM rocloud_dev_user;
GRANT  INSERT, SELECT             ON audit_logs TO   rocloud_dev_user;

-- Existing monthly partitions inherit privileges from the parent, but be explicit for any
-- already created out-of-band:
DO $$
DECLARE part text;
BEGIN
    FOR part IN
        SELECT inhrelid::regclass::text
        FROM pg_inherits
        WHERE inhparent = 'audit_logs'::regclass
    LOOP
        EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON %s FROM rocloud_dev_user', part);
        EXECUTE format('GRANT  INSERT, SELECT            ON %s TO   rocloud_dev_user', part);
    END LOOP;
END $$;
