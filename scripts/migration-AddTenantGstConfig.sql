-- ============================================================================
-- Owner-configurable GST on customer invoices (guide §24).
-- Adds an on/off toggle and an editable rate to the tenant. Rate is stored as a
-- fraction (0.18 = 18%) to match the invoice generation math; the owner portal
-- shows/edits it as a percentage. Defaults preserve today's behaviour (GST on @ 18%).
--
-- 'tenants' is postgres-owned — run as the postgres superuser:
--   "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -d rocloud_dev -f scripts\migration-AddTenantGstConfig.sql
-- ============================================================================

ALTER TABLE tenants
    ADD COLUMN IF NOT EXISTS gst_enabled BOOLEAN      NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS gst_rate    NUMERIC(5,4) NOT NULL DEFAULT 0.18;
