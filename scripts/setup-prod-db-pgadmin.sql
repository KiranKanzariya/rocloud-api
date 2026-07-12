-- ════════════════════════════════════════════════════════════════════════════
-- Create a SEPARATE production database — pgAdmin runbook.
--
-- pgAdmin's Query Tool runs SQL only; it can't pg_dump|psql-clone a schema. And the hand-written
-- schema.sql has DRIFTED from the live database, so the reliable way to copy the *current* structure
-- is pgAdmin's own Backup/Restore (which uses pg_dump under the hood). Reference data (plans,
-- permissions, audit settings) and the role/grants are seeded by THIS script.
--
-- ── DO THESE IN ORDER ───────────────────────────────────────────────────────
-- All steps: connect in pgAdmin as the **postgres** superuser.
--
-- STEP 1 — Create the database.
--   Open a Query Tool on the "postgres" database and run JUST this line (autocommit):
--       CREATE DATABASE rocloud_prod;
--   (or right-click Databases → Create → Database…, name it rocloud_prod)
--
-- STEP 2 — Clone the STRUCTURE from rocloud_dev (no data) via Backup/Restore:
--   a) Right-click the "rocloud_dev" database → Backup…
--        • Format: Custom        • Filename: rocloud_schema.backup
--        • Tab "Data/Objects": turn ON "Only schema"
--        • Tab "Options" → "Do not save": turn ON "Owner" and "Privileges"
--        • (Optional) "Do not save" → turn ON "Tablespaces"
--      → Backup.
--   b) Right-click the new "rocloud_prod" database → Restore…
--        • Format: Custom, Filename: rocloud_schema.backup
--        • "Do not save": Owner = ON, Privileges = ON
--      → Restore.  (Extensions, tables, indexes, RLS policies and the audit_logs
--        partitions all come across; everything is owned by postgres.)
--
-- STEP 3 — Open a Query Tool ON "rocloud_prod" and run THIS WHOLE script.
--          ⚠ Edit the role password on the CREATE ROLE line below first.
--
-- STEP 4 — Create a platform admin (edit email/password inside the file first):
--          run scripts/create-platform-admin.sql against rocloud_prod.
--
-- STEP 5 — Point production at the new DB: in C:\inetpub\rocloud-api\web.config set
--          ConnectionStrings__Default to
--            Host=localhost;Port=5432;Database=rocloud_prod;Username=rocloud_app;Password=<the password below>;
--          then recycle the IIS app pool. Dev keeps using rocloud_dev.
--
-- ── PURE-SQL ALTERNATIVE (copies ALL current data, not just schema) ──────────
--   If you actually want prod to start as an EXACT copy of the current data, skip
--   steps 2–3's seed and instead — with the API stopped so nothing is connected to
--   rocloud_dev — run on the "postgres" database:
--       CREATE DATABASE rocloud_prod TEMPLATE rocloud_dev;
--   then run only the ROLE & GRANTS section below. (Requires zero open connections
--   to rocloud_dev, so put the API in app_offline first.)
-- ════════════════════════════════════════════════════════════════════════════


-- ─────────────────────────────────────────────────────────────────────────────
-- ROLE & GRANTS  (run connected to rocloud_prod, as postgres)
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'rocloud_app') THEN
        CREATE ROLE rocloud_app LOGIN PASSWORD 'CHANGE_ME_STRONG_PASSWORD';   -- ← EDIT THIS
    END IF;
END
$$;

GRANT CONNECT ON DATABASE rocloud_prod TO rocloud_app;
GRANT USAGE, CREATE ON SCHEMA public TO rocloud_app;
GRANT ALL PRIVILEGES ON ALL TABLES    IN SCHEMA public TO rocloud_app;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO rocloud_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL PRIVILEGES ON TABLES    TO rocloud_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL PRIVILEGES ON SEQUENCES TO rocloud_app;

-- Hangfire (the API installs its own tables in this schema on first run).
CREATE SCHEMA IF NOT EXISTS hangfire;
GRANT ALL ON SCHEMA hangfire TO rocloud_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON TABLES    TO rocloud_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON SEQUENCES TO rocloud_app;

-- audit_logs is append-only: the app may INSERT/SELECT but never UPDATE/DELETE.
REVOKE UPDATE, DELETE, TRUNCATE ON audit_logs FROM rocloud_app;
GRANT  INSERT, SELECT             ON audit_logs TO   rocloud_app;


-- ─────────────────────────────────────────────────────────────────────────────
-- REFERENCE DATA  (skip this whole section if you used the TEMPLATE clone)
-- Mirrors the live rocloud_dev plans/permissions so registration works on day one.
-- ─────────────────────────────────────────────────────────────────────────────

-- Permissions (idempotent on the unique code).
INSERT INTO permissions (module, action, code) VALUES
  ('AMC', 'Manage', 'AMC.Manage'),
  ('AMC', 'Update', 'AMC.Update'),
  ('AMC', 'View', 'AMC.View'),
  ('Customers', 'Create', 'Customers.Create'),
  ('Customers', 'Delete', 'Customers.Delete'),
  ('Customers', 'Edit', 'Customers.Edit'),
  ('Customers', 'View', 'Customers.View'),
  ('Deliveries', 'Update', 'Deliveries.Update'),
  ('Deliveries', 'View', 'Deliveries.View'),
  ('Deliveries', 'ViewOwn', 'Deliveries.ViewOwn'),
  ('Inventory', 'Manage', 'Inventory.Manage'),
  ('Inventory', 'View', 'Inventory.View'),
  ('Invoices', 'Create', 'Invoices.Create'),
  ('Invoices', 'Edit', 'Invoices.Edit'),
  ('Invoices', 'View', 'Invoices.View'),
  ('Orders', 'Cancel', 'Orders.Cancel'),
  ('Orders', 'Create', 'Orders.Create'),
  ('Orders', 'Edit', 'Orders.Edit'),
  ('Orders', 'View', 'Orders.View'),
  ('Payments', 'Collect', 'Payments.Collect'),
  ('Payments', 'Manage', 'Payments.Manage'),
  ('Payments', 'View', 'Payments.View'),
  ('Reports', 'View', 'Reports.View'),
  ('Roles', 'Manage', 'Roles.Manage'),
  ('Roles', 'View', 'Roles.View'),
  -- One View/Manage pair per settings PAGE. The tenant's own ROCloud subscription has no permission
  -- at all — it is [RequireOwner], so no custom role can be granted the right to change the plan.
  ('Areas', 'View', 'Areas.View'),
  ('Areas', 'Manage', 'Areas.Manage'),
  ('Notifications', 'View', 'Notifications.View'),
  ('Notifications', 'Manage', 'Notifications.Manage'),
  ('Business Profile', 'View', 'BusinessProfile.View'),
  ('Business Profile', 'Manage', 'BusinessProfile.Manage'),
  ('Users', 'Manage', 'Users.Manage'),
  ('Users', 'View', 'Users.View')
ON CONFLICT (code) DO NOTHING;

-- Plans (only when the table is empty, so re-runs don't duplicate).
-- max_customers / max_users / max_delivery_boys: 0 = unlimited (Plan.Unlimited).
-- whatsapp_enabled, multi_branch_enabled, api_access_enabled are false on every plan:
-- those features are not built yet and both portals render them as "coming soon".
INSERT INTO plans (name, plan_type, monthly_price, yearly_price, max_customers, max_users,
                   max_delivery_boys, whatsapp_enabled, custom_roles_enabled, multi_branch_enabled,
                   api_access_enabled, is_active)
SELECT * FROM (VALUES
  ('Basic',      'Basic',      1099.00,  9990.00, 200,  3, 1, false, false, false, false, true),
  ('Pro',        'Pro',        2499.00, 24990.00, 1000, 10, 5, false, false, false, false, true),
  ('Enterprise', 'Enterprise', 5999.00, 59990.00, 0,   0, 0, false, true,  false, false, true)
) AS v(name, plan_type, monthly_price, yearly_price, max_customers, max_users,
       max_delivery_boys, whatsapp_enabled, custom_roles_enabled, multi_branch_enabled,
       api_access_enabled, is_active)
WHERE NOT EXISTS (SELECT 1 FROM plans);

-- Activity-log settings: ensure the single global row exists (defaults mirror today's behaviour).
INSERT INTO audit_settings (id)
SELECT uuid_generate_v4()
WHERE NOT EXISTS (SELECT 1 FROM audit_settings);


-- Sanity check.
SELECT
  (SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public') AS tables,
  (SELECT count(*) FROM pg_policies)                                             AS rls_policies,
  (SELECT count(*) FROM plans)                                                   AS plans,
  (SELECT count(*) FROM permissions)                                             AS permissions,
  (SELECT count(*) FROM audit_settings)                                          AS audit_settings;
