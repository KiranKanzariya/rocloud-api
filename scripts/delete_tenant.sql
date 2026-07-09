-- ─────────────────────────────────────────────────────────────────────────────
-- delete_tenant.sql — completely remove ONE tenant and all its data.
--
-- MUST be run as the postgres superuser: orders / deliveries / audit_logs have
-- row-level security, and only a superuser (or the table owner) bypasses it.
-- Running as rocloud_dev_user would silently skip those rows and leave orphans.
--
-- Usage (pick the tenant by its subdomain):
--   psql -U postgres -d rocloud_dev -v sub='acmewater1781856483' -f delete_tenant.sql
--
-- It runs inside a transaction and COMMITs at the end. To preview without
-- deleting, change the final COMMIT to ROLLBACK and run it.
--
-- This schema has NO foreign keys, so statement order is not load-bearing; the
-- tenants row is still deleted LAST so the subqueries can resolve its id.
-- ─────────────────────────────────────────────────────────────────────────────

\set ON_ERROR_STOP on
BEGIN;

-- Fail loudly if the subdomain doesn't exist (avoids a silent no-op delete).
SELECT id AS tenant_id, name FROM tenants WHERE subdomain = :'sub' \gset
\if :{?tenant_id}
\else
  \echo '!!! No tenant with subdomain' :'sub'
  \quit
\endif
\echo 'Deleting tenant:' :'name' '(' :'sub' ')'

-- Order matters: this schema HAS foreign keys (mostly RESTRICT). Children first.
DELETE FROM payments                    WHERE tenant_id = :'tenant_id';
DELETE FROM delivery_items              WHERE tenant_id = :'tenant_id';
DELETE FROM inventory_movements         WHERE tenant_id = :'tenant_id';
DELETE FROM deliveries                  WHERE tenant_id = :'tenant_id';
DELETE FROM order_items                 WHERE tenant_id = :'tenant_id';
DELETE FROM invoices                    WHERE tenant_id = :'tenant_id';
DELETE FROM customer_subscriptions      WHERE tenant_id = :'tenant_id';
DELETE FROM amc_subscriptions           WHERE tenant_id = :'tenant_id';
DELETE FROM service_requests            WHERE tenant_id = :'tenant_id';
DELETE FROM notifications               WHERE tenant_id = :'tenant_id';
DELETE FROM inventory                   WHERE tenant_id = :'tenant_id';
DELETE FROM orders                      WHERE tenant_id = :'tenant_id';
DELETE FROM user_areas                  WHERE tenant_id = :'tenant_id';
DELETE FROM support_tickets             WHERE tenant_id = :'tenant_id';
DELETE FROM notification_templates      WHERE tenant_id = :'tenant_id';
DELETE FROM platform_billing_transactions WHERE tenant_id = :'tenant_id';

-- audit_logs.tenant_id is NULLABLE but audit_logs.user_id -> users is enforced; delete by BOTH
-- keys (auth/login rows for this tenant's users may carry a NULL tenant_id). Partitioned → parent covers all.
DELETE FROM audit_logs WHERE tenant_id = :'tenant_id'
                          OR user_id IN (SELECT id FROM users WHERE tenant_id = :'tenant_id');

DELETE FROM customers                   WHERE tenant_id = :'tenant_id';  -- before areas
DELETE FROM products                    WHERE tenant_id = :'tenant_id';
DELETE FROM areas                       WHERE tenant_id = :'tenant_id';

-- role_permissions has no tenant_id — scope via the tenant's roles.
DELETE FROM role_permissions WHERE role_id IN (SELECT id FROM roles WHERE tenant_id = :'tenant_id');

DELETE FROM users                       WHERE tenant_id = :'tenant_id';
DELETE FROM roles                       WHERE tenant_id = :'tenant_id';
DELETE FROM tenants                     WHERE id = :'tenant_id';         -- last

COMMIT;
\echo 'Done.'
