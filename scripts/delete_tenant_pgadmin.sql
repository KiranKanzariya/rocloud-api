-- ─────────────────────────────────────────────────────────────────────────────
-- delete_tenant_pgadmin.sql — remove ONE tenant and all its data, from pgAdmin.
--
-- HOW TO RUN:
--   1. In pgAdmin, connect to the database as the `postgres` superuser
--      (REQUIRED — orders/deliveries/audit_logs have row-level security that
--       only a superuser bypasses; a normal role silently skips those rows).
--   2. Open the Query Tool on the rocloud database.
--   3. Change the subdomain on the line marked <<< below.
--   4. Run (F5). It's one DO block = one transaction: if anything errors,
--      the whole thing rolls back automatically.
--
-- TIP: run the "PREVIEW" query at the bottom first to see what will be removed.
-- ─────────────────────────────────────────────────────────────────────────────

DO $$
DECLARE
  v_sub  text := 'acmewater1781856483';   -- <<< CHANGE THIS to the tenant's subdomain
  v_tid  uuid;
  v_name text;
BEGIN
  SELECT id, name INTO v_tid, v_name FROM tenants WHERE subdomain = v_sub;
  IF v_tid IS NULL THEN
    RAISE EXCEPTION 'No tenant with subdomain %', v_sub;
  END IF;

  -- Order matters: this schema HAS foreign keys (mostly RESTRICT between tables),
  -- so children are deleted before their parents. Deepest dependents first.
  DELETE FROM payments                      WHERE tenant_id = v_tid;
  DELETE FROM delivery_items                WHERE tenant_id = v_tid;
  DELETE FROM inventory_movements           WHERE tenant_id = v_tid;
  DELETE FROM deliveries                    WHERE tenant_id = v_tid;
  DELETE FROM order_items                   WHERE tenant_id = v_tid;
  DELETE FROM invoices                      WHERE tenant_id = v_tid;
  DELETE FROM customer_subscriptions        WHERE tenant_id = v_tid;
  DELETE FROM amc_subscriptions             WHERE tenant_id = v_tid;
  DELETE FROM service_requests              WHERE tenant_id = v_tid;
  DELETE FROM notifications                 WHERE tenant_id = v_tid;
  DELETE FROM inventory                     WHERE tenant_id = v_tid;
  DELETE FROM orders                        WHERE tenant_id = v_tid;
  DELETE FROM user_areas                    WHERE tenant_id = v_tid;
  DELETE FROM support_tickets               WHERE tenant_id = v_tid;
  DELETE FROM notification_templates        WHERE tenant_id = v_tid;
  DELETE FROM platform_billing_transactions WHERE tenant_id = v_tid;

  -- audit_logs.tenant_id is NULLABLE, but audit_logs.user_id -> users is enforced.
  -- Login/auth rows for this tenant's users may have a NULL tenant_id, so delete by
  -- BOTH keys or the users delete below fails on audit_logs_user_id_fkey.
  -- (audit_logs is partitioned; deleting from the parent covers all partitions.)
  DELETE FROM audit_logs
   WHERE tenant_id = v_tid
      OR user_id IN (SELECT id FROM users WHERE tenant_id = v_tid);

  DELETE FROM customers                     WHERE tenant_id = v_tid;  -- before areas (customers.area_id -> areas)
  DELETE FROM products                      WHERE tenant_id = v_tid;
  DELETE FROM areas                         WHERE tenant_id = v_tid;

  -- role_permissions has no tenant_id — scope via the tenant's roles (before users/roles).
  DELETE FROM role_permissions WHERE role_id IN (SELECT id FROM roles WHERE tenant_id = v_tid);

  DELETE FROM users                         WHERE tenant_id = v_tid;  -- after audit_logs
  DELETE FROM roles                         WHERE tenant_id = v_tid;  -- after users (users.role_id -> roles)
  DELETE FROM tenants                       WHERE id = v_tid;          -- last

  RAISE NOTICE 'Deleted tenant "%" (subdomain %, id %)', v_name, v_sub, v_tid;
END $$;


-- ─── PREVIEW (optional) ──────────────────────────────────────────────────────
-- Run this BEFORE the block above to see the row counts that will be deleted.
-- Change the subdomain to match.
/*
WITH t AS (SELECT id FROM tenants WHERE subdomain = 'acmewater1781856483')
SELECT 'customers' AS table, count(*) FROM customers   WHERE tenant_id = (SELECT id FROM t)
UNION ALL SELECT 'orders',        count(*) FROM orders        WHERE tenant_id = (SELECT id FROM t)
UNION ALL SELECT 'deliveries',    count(*) FROM deliveries    WHERE tenant_id = (SELECT id FROM t)
UNION ALL SELECT 'invoices',      count(*) FROM invoices      WHERE tenant_id = (SELECT id FROM t)
UNION ALL SELECT 'payments',      count(*) FROM payments      WHERE tenant_id = (SELECT id FROM t)
UNION ALL SELECT 'users',         count(*) FROM users         WHERE tenant_id = (SELECT id FROM t)
ORDER BY 1;
*/

-- ─── LIST test tenants (optional) ────────────────────────────────────────────
-- Keep your real/demo tenants off the list before deleting.
/*
SELECT subdomain, name, created_at
FROM tenants
WHERE subdomain NOT IN ('sharma-ro', 'akash-ro')
ORDER BY created_at;
*/
