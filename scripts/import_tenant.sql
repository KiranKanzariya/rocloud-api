-- ─────────────────────────────────────────────────────────────────────────────
-- import_tenant.sql — load ONE tenant exported by export_tenant.sql INTO a
-- fresh prod database.
--
-- MUST be run as the postgres superuser (bypasses RLS + can insert explicit uuids).
-- Run from the folder that CONTAINS ./tenant_export/ (the CSVs).
--
--   psql -U postgres -d rocloud_prod -f import_tenant.sql
--
-- Runs in ONE transaction: if anything fails, nothing is committed. Change the
-- final COMMIT to ROLLBACK to do a dry run first.
--
-- PRECONDITION: prod schema already built (schema.sql + all migration-*.sql).
-- The schema seeds plans + permissions with DIFFERENT uuids than dev, so we
-- REPLACE them here with dev's copies to keep every FK valid. This is only safe
-- on a FRESH prod with no other tenants referencing those seeded rows.
--
-- Load order = parents before children (FKs are mostly RESTRICT).
-- ─────────────────────────────────────────────────────────────────────────────

\set ON_ERROR_STOP on
BEGIN;

-- ── Replace seeded platform reference data with dev's (keeps UUIDs aligned) ────
-- role_permissions / tenants reference these; they are loaded further below, so
-- clearing here is safe within the transaction. TRUNCATE ... RESTRICT would trip
-- on the seeded FKs only if child rows already exist — on fresh prod there are none.
DELETE FROM role_permissions;   -- clear seed-linked rows first (none on fresh prod, but explicit)
DELETE FROM permissions;
DELETE FROM plans;
\copy plans       FROM 'tenant_export/plans.csv'       CSV
\copy permissions FROM 'tenant_export/permissions.csv' CSV

-- ── Tenant ────────────────────────────────────────────────────────────────────
\copy tenants FROM 'tenant_export/tenants.csv' CSV

-- ── RBAC ──────────────────────────────────────────────────────────────────────
\copy roles            FROM 'tenant_export/roles.csv'            CSV
\copy role_permissions FROM 'tenant_export/role_permissions.csv' CSV
\copy users            FROM 'tenant_export/users.csv'            CSV

-- ── Geography & catalog ───────────────────────────────────────────────────────
\copy areas      FROM 'tenant_export/areas.csv'      CSV
\copy user_areas FROM 'tenant_export/user_areas.csv' CSV
\copy products   FROM 'tenant_export/products.csv'   CSV

-- ── Customers & subscriptions ─────────────────────────────────────────────────
\copy customers              FROM 'tenant_export/customers.csv'              CSV
\copy customer_subscriptions FROM 'tenant_export/customer_subscriptions.csv' CSV
\copy amc_subscriptions      FROM 'tenant_export/amc_subscriptions.csv'      CSV

-- ── Orders (order_items column list must match export: no total_amount) ────────
\copy orders FROM 'tenant_export/orders.csv' CSV
\copy order_items (id, tenant_id, order_id, product_id, quantity, unit_rate) FROM 'tenant_export/order_items.csv' CSV

-- ── Inventory ─────────────────────────────────────────────────────────────────
\copy inventory           FROM 'tenant_export/inventory.csv'           CSV
\copy inventory_movements FROM 'tenant_export/inventory_movements.csv' CSV

-- ── Deliveries ────────────────────────────────────────────────────────────────
\copy deliveries     FROM 'tenant_export/deliveries.csv'     CSV
\copy delivery_items FROM 'tenant_export/delivery_items.csv' CSV

-- ── Billing ───────────────────────────────────────────────────────────────────
\copy invoices FROM 'tenant_export/invoices.csv' CSV
\copy payments FROM 'tenant_export/payments.csv' CSV

-- ── Service / AMC ─────────────────────────────────────────────────────────────
\copy service_requests FROM 'tenant_export/service_requests.csv' CSV

-- ── Templates & platform billing ──────────────────────────────────────────────
\copy notification_templates        FROM 'tenant_export/notification_templates.csv'        CSV
\copy support_tickets               FROM 'tenant_export/support_tickets.csv'               CSV
\copy platform_billing_transactions FROM 'tenant_export/platform_billing_transactions.csv' CSV

-- ── OPTIONAL — only if you exported them (see export_tenant.sql) ───────────────
-- \copy notifications FROM 'tenant_export/notifications.csv' CSV
-- \copy reminder_log  FROM 'tenant_export/reminder_log.csv'  CSV

COMMIT;
\echo 'Import complete.'
