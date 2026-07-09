-- ============================================================================
-- Adds a standing discount on a tenant's ROCloud SUBSCRIPTION price (guide §26).
-- Platform-set (super-admin only). Separate from the per-customer water-invoice
-- discount (customers.discount_type) added by migration-AddCustomerDiscount.sql.
--
-- 'tenants' is postgres-owned — run as the postgres superuser:
--   "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -d rocloud_dev -f scripts\migration-AddTenantSubscriptionDiscount.sql
--
-- Free months are NOT a column — they are granted by extending subscription_ends_at.
-- ============================================================================

ALTER TABLE tenants
    ADD COLUMN IF NOT EXISTS subscription_discount_type  VARCHAR(20)  NOT NULL DEFAULT 'None'
        CHECK (subscription_discount_type IN ('None', 'Percentage', 'Fixed')),
    ADD COLUMN IF NOT EXISTS subscription_discount_value NUMERIC(10,2) NOT NULL DEFAULT 0;
