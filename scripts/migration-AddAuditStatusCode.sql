-- Adds the response status code to the audit log so the Activity viewer can show whether each
-- action succeeded or failed. Backward-compatible: the column is nullable, so existing rows and any
-- writes made before the new code is deployed simply leave it NULL.
--
-- audit_logs is owned by `postgres` and is range-partitioned; run this as the postgres superuser.
-- ALTER on the partitioned parent propagates to all partitions.
--
--   psql -U postgres -d rocloud_dev -f scripts/migration-AddAuditStatusCode.sql

ALTER TABLE audit_logs ADD COLUMN IF NOT EXISTS status_code INTEGER;
