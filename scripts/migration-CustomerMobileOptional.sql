-- Make customers.mobile optional so RO owners can import customers they have no number for.
-- The mobile stays the preferred identity/dedupe key when present; when absent, the customer is
-- identified by its CUST- code and de-duplicated by name (import only). Reminder jobs already skip
-- customers without a mobile, so this is safe.
--
-- customers has RLS, so RUN THIS AS THE postgres SUPERUSER, and BEFORE deploying the new API build
-- (the new code may insert NULL mobiles, which the old NOT NULL constraint would reject):
--   psql -U postgres -d rocloud_dev -f migration-CustomerMobileOptional.sql

ALTER TABLE customers ALTER COLUMN mobile DROP NOT NULL;
