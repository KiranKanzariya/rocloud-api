-- ─────────────────────────────────────────────────────────────────────────────
-- export_tenant.sql — pull ONE tenant (owner) and all its data OUT of a database
-- as portable CSV files, so it can be loaded into a FRESH prod DB.
--
-- This is the inverse of delete_tenant.sql. Same rules apply:
-- MUST be run as the postgres superuser — orders / deliveries / audit_logs /
-- customers / invoices / payments have row-level security, and only a superuser
-- (or table owner) reads ALL rows. Running as the app user would silently export
-- an empty / partial set.
--
-- Usage (pick the owner by subdomain, from a directory you can write to):
--   mkdir tenant_export
--   psql -U postgres -d rocloud_dev -v sub='acmewater1781856483' -f export_tenant.sql
--
-- Produces CSV files under ./tenant_export/ . Copy that whole folder to the prod
-- host, then run import_tenant.sql there. CSV is used (not pg_dump) so it works
-- even when prod is a different server / Supabase.
--
-- NOTE ON plans + permissions: these are platform-wide reference tables seeded
-- with RANDOM uuids per database. We export them here and REPLACE prod's seeded
-- copies on import, so tenant.plan_id and role_permissions.permission_id stay
-- valid without any UUID remapping. This assumes prod is FRESH (no other live
-- tenants whose data references its existing plan/permission rows).
-- ─────────────────────────────────────────────────────────────────────────────

\set ON_ERROR_STOP on

-- Resolve the tenant id from the subdomain; fail loudly if it doesn't exist.
SELECT id AS tid, name FROM tenants WHERE subdomain = :'sub' \gset
\if :{?tid}
\else
  \echo '!!! No tenant with subdomain' :'sub'
  \quit
\endif
\echo 'Exporting tenant:' :'name' '(' :'sub' ')  id=' :'tid'

-- ── Platform reference tables (full — small, needed for FK integrity) ──────────
\copy (SELECT * FROM plans ORDER BY created_at)       TO 'tenant_export/plans.csv'       CSV
\copy (SELECT * FROM permissions ORDER BY code)       TO 'tenant_export/permissions.csv' CSV

-- ── The tenant row itself ─────────────────────────────────────────────────────
\copy (SELECT * FROM tenants WHERE id = :'tid')        TO 'tenant_export/tenants.csv'     CSV

-- ── RBAC ──────────────────────────────────────────────────────────────────────
\copy (SELECT * FROM roles WHERE tenant_id = :'tid')   TO 'tenant_export/roles.csv'       CSV
\copy (SELECT rp.* FROM role_permissions rp JOIN roles r ON r.id = rp.role_id WHERE r.tenant_id = :'tid') TO 'tenant_export/role_permissions.csv' CSV
\copy (SELECT * FROM users WHERE tenant_id = :'tid')   TO 'tenant_export/users.csv'       CSV

-- ── Geography & catalog ───────────────────────────────────────────────────────
\copy (SELECT * FROM areas WHERE tenant_id = :'tid')     TO 'tenant_export/areas.csv'      CSV
\copy (SELECT * FROM user_areas WHERE tenant_id = :'tid') TO 'tenant_export/user_areas.csv' CSV
\copy (SELECT * FROM products WHERE tenant_id = :'tid')   TO 'tenant_export/products.csv'   CSV

-- ── Customers & their subscriptions ───────────────────────────────────────────
\copy (SELECT * FROM customers WHERE tenant_id = :'tid')              TO 'tenant_export/customers.csv'              CSV
\copy (SELECT * FROM customer_subscriptions WHERE tenant_id = :'tid') TO 'tenant_export/customer_subscriptions.csv' CSV
\copy (SELECT * FROM amc_subscriptions WHERE tenant_id = :'tid')      TO 'tenant_export/amc_subscriptions.csv'      CSV

-- ── Orders (order_items EXCLUDES the generated total_amount column) ────────────
\copy (SELECT * FROM orders WHERE tenant_id = :'tid') TO 'tenant_export/orders.csv' CSV
\copy (SELECT id, tenant_id, order_id, product_id, quantity, unit_rate FROM order_items WHERE tenant_id = :'tid') TO 'tenant_export/order_items.csv' CSV

-- ── Inventory ─────────────────────────────────────────────────────────────────
\copy (SELECT * FROM inventory WHERE tenant_id = :'tid')           TO 'tenant_export/inventory.csv'           CSV
\copy (SELECT * FROM inventory_movements WHERE tenant_id = :'tid') TO 'tenant_export/inventory_movements.csv' CSV

-- ── Deliveries ────────────────────────────────────────────────────────────────
\copy (SELECT * FROM deliveries WHERE tenant_id = :'tid')     TO 'tenant_export/deliveries.csv'     CSV
\copy (SELECT * FROM delivery_items WHERE tenant_id = :'tid') TO 'tenant_export/delivery_items.csv' CSV

-- ── Billing ───────────────────────────────────────────────────────────────────
\copy (SELECT * FROM invoices WHERE tenant_id = :'tid') TO 'tenant_export/invoices.csv' CSV
\copy (SELECT * FROM payments WHERE tenant_id = :'tid') TO 'tenant_export/payments.csv' CSV

-- ── Service / AMC tickets ─────────────────────────────────────────────────────
\copy (SELECT * FROM service_requests WHERE tenant_id = :'tid') TO 'tenant_export/service_requests.csv' CSV

-- ── Templates & platform billing ──────────────────────────────────────────────
\copy (SELECT * FROM notification_templates WHERE tenant_id = :'tid')       TO 'tenant_export/notification_templates.csv'       CSV
\copy (SELECT * FROM support_tickets WHERE tenant_id = :'tid')              TO 'tenant_export/support_tickets.csv'              CSV
\copy (SELECT * FROM platform_billing_transactions WHERE tenant_id = :'tid') TO 'tenant_export/platform_billing_transactions.csv' CSV

-- ── OPTIONAL / transient — uncomment only if you truly want history carried over.
--    notifications  → regenerated by the background jobs from live state.
--    reminder_log   → throttle bookkeeping; safe to start empty.
--    audit_logs     → partitioned by month; prod must have partitions covering
--                     these dates or the load fails. Usually not worth migrating.
-- \copy (SELECT * FROM notifications WHERE tenant_id = :'tid') TO 'tenant_export/notifications.csv' CSV
-- \copy (SELECT * FROM reminder_log  WHERE tenant_id = :'tid') TO 'tenant_export/reminder_log.csv'  CSV

\echo 'Export complete → ./tenant_export/'
