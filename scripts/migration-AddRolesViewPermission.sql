-- migration-AddRolesViewPermission.sql
--
-- Adds the Roles.View permission and grants it to every existing role that can already see users.
--
-- WHY: reading role names was gated behind Roles.Manage, so any role holding Users.View (Manager,
-- Viewer, custom roles) got a 403 simply by opening the Users page, and a custom role with
-- Users.Manage but no Roles.Manage could open the "Add user" dialog to an empty role dropdown.
--
-- RUN AS: postgres. The permissions and role_permissions tables are owned by postgres (RLS), so
-- the application role cannot INSERT into them.
--
-- RUN IN: every database — rocloud_dev AND rocloud_prod.
--   psql -U postgres -d rocloud_dev  -f scripts/migration-AddRolesViewPermission.sql
--   psql -U postgres -d rocloud_prod -f scripts/migration-AddRolesViewPermission.sql
--
-- Idempotent: safe to run more than once.

BEGIN;

-- 1. The permission itself. Fixed id keeps it identical to seed-reference-data.sql.
INSERT INTO public.permissions (id, module, action, code)
VALUES ('2c1f7f4e-3a5d-4b8e-9c17-6d0a1b2e4c93', 'Roles', 'View', 'Roles.View')
ON CONFLICT (code) DO NOTHING;

-- 2. Grant it to every role that already holds Users.View, in every tenant. Roles are per-tenant,
--    so this is one row per (tenant, role) — not a single global grant.
INSERT INTO public.role_permissions (role_id, permission_id)
SELECT rp.role_id, (SELECT id FROM public.permissions WHERE code = 'Roles.View')
FROM public.role_permissions rp
JOIN public.permissions p ON p.id = rp.permission_id
WHERE p.code = 'Users.View'
ON CONFLICT DO NOTHING;

-- 3. Owners hold every permission, but they are seeded by enumeration rather than a wildcard row,
--    so grant it to them explicitly too (an Owner without Roles.View would lose the roles page).
INSERT INTO public.role_permissions (role_id, permission_id)
SELECT r.id, (SELECT id FROM public.permissions WHERE code = 'Roles.View')
FROM public.roles r
WHERE r.name = 'Owner'
ON CONFLICT DO NOTHING;

COMMIT;

-- Verification — every row should report has_roles_view = true.
--
-- SELECT t.subdomain, r.name AS role,
--        bool_or(p.code = 'Users.View') AS has_users_view,
--        bool_or(p.code = 'Roles.View') AS has_roles_view
-- FROM public.roles r
-- JOIN public.tenants t ON t.id = r.tenant_id
-- JOIN public.role_permissions rp ON rp.role_id = r.id
-- JOIN public.permissions p ON p.id = rp.permission_id
-- GROUP BY t.subdomain, r.name
-- HAVING bool_or(p.code = 'Users.View')
-- ORDER BY t.subdomain, r.name;
