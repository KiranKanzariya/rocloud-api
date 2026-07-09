-- Phase 26b: per-customer standing discount (platform-managed, guide §26).
-- customers is owned by the postgres superuser (created by schema.sql) and has RLS, so run this
-- AS THE DATABASE OWNER (postgres):
--   psql -U postgres -d rocloud_dev -f migration-AddCustomerDiscount.sql

ALTER TABLE customers
    ADD COLUMN IF NOT EXISTS discount_type  VARCHAR(20) NOT NULL DEFAULT 'None'
        CHECK (discount_type IN ('None', 'Percentage', 'Fixed')),
    ADD COLUMN IF NOT EXISTS discount_value NUMERIC(10,2) NOT NULL DEFAULT 0;
