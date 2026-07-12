-- migration-AddPageWiseSettingsPermissions.sql
--
-- Splits the two blunt Settings permissions into one View/Manage pair per settings PAGE, and takes
-- the ROCloud subscription out of the permission system entirely.
--
-- WHY: Settings.View / Settings.Manage covered four unrelated pages at once — delivery areas,
-- notification templates, the business profile, AND the tenant's own ROCloud subscription. Granting a
-- manager the right to add a delivery area therefore also let them change your plan and pay with your
-- card. Users, Roles and Inventory were already their own permission modules despite living under the
-- Settings nav; this finishes that job.
--
--   Settings.View    →  Areas.View    + Notifications.View    + BusinessProfile.View
--   Settings.Manage  →  Areas.Manage  + Notifications.Manage  + BusinessProfile.Manage
--   Subscription     →  no permission at all; the API now uses [RequireOwner], like the Activity log.
--
-- Existing roles keep exactly the access they had: the back-fill below maps each old permission onto
-- its three successors before the old rows are dropped. Nobody loses a page on deploy.
--
-- RUN AS: postgres. permissions / role_permissions are owned by postgres (RLS), so the application
-- role cannot write to them.
--
-- RUN IN: every database — rocloud_dev AND rocloud_prod.
--   psql -U postgres -d rocloud_dev  -f scripts/migration-AddPageWiseSettingsPermissions.sql
--   psql -U postgres -d rocloud_prod -f scripts/migration-AddPageWiseSettingsPermissions.sql
--
-- DEPLOY ORDER: run this BEFORE (or together with) the API deploy — the new API asks for the new
-- codes and no longer asks for the old ones. Users must sign in again (or refresh) to pick the new
-- codes up: permissions are a snapshot baked into the JWT at login, not read per request.
--
-- Idempotent: safe to run more than once.

BEGIN;

-- 1. The six new permissions. Fixed ids keep them identical to seed-reference-data.sql.
INSERT INTO public.permissions (id, module, action, code) VALUES
    ('3f2b9c14-6d81-4a27-9e53-1c8f0b6a2d47', 'Areas',            'View',   'Areas.View'),
    ('8a15e7d2-0c64-4b39-a7f8-52d9e3b06c1a', 'Areas',            'Manage', 'Areas.Manage'),
    ('c94d63f8-27ba-4e15-8306-4f7a1e9d5b28', 'Notifications',    'View',   'Notifications.View'),
    ('5e07a1b6-9f3d-4c82-b1e4-7036c8d4a9f5', 'Notifications',    'Manage', 'Notifications.Manage'),
    ('b6c8f430-1e59-42d7-9a06-3d8b5f2c7e41', 'Business Profile', 'View',   'BusinessProfile.View'),
    ('2d94a7e5-8b36-4f01-93c7-6a1e0d5b8c93', 'Business Profile', 'Manage', 'BusinessProfile.Manage')
ON CONFLICT (code) DO NOTHING;

-- 2. Back-fill: every role that could READ settings can now read all three settings pages.
--    Roles are per-tenant, so this is one row per (tenant, role) — not a single global grant.
INSERT INTO public.role_permissions (role_id, permission_id)
SELECT rp.role_id, np.id
FROM public.role_permissions rp
JOIN public.permissions op ON op.id = rp.permission_id AND op.code = 'Settings.View'
CROSS JOIN public.permissions np
WHERE np.code IN ('Areas.View', 'Notifications.View', 'BusinessProfile.View')
ON CONFLICT DO NOTHING;

-- 3. Back-fill: every role that could WRITE settings can now write all three settings pages.
--    Note the subscription is deliberately NOT in this list — it is Owner-only from now on.
INSERT INTO public.role_permissions (role_id, permission_id)
SELECT rp.role_id, np.id
FROM public.role_permissions rp
JOIN public.permissions op ON op.id = rp.permission_id AND op.code = 'Settings.Manage'
CROSS JOIN public.permissions np
WHERE np.code IN ('Areas.Manage', 'Notifications.Manage', 'BusinessProfile.Manage')
ON CONFLICT DO NOTHING;

-- 4. Owners hold every permission, but they are seeded by enumeration rather than a wildcard row, so
--    grant the new ones explicitly (an Owner missing BusinessProfile.View would lose their own
--    settings page).
INSERT INTO public.role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM public.roles r
CROSS JOIN public.permissions p
WHERE r.name = 'Owner'
  AND p.code IN ('Areas.View', 'Areas.Manage',
                 'Notifications.View', 'Notifications.Manage',
                 'BusinessProfile.View', 'BusinessProfile.Manage')
ON CONFLICT DO NOTHING;

-- 5. Retire the old codes. Nothing asks for them any more, and leaving them would show a dead
--    "Settings" group of checkboxes on the Roles page. Grants go first (FK), then the rows.
DELETE FROM public.role_permissions
WHERE permission_id IN (
    SELECT id FROM public.permissions WHERE code IN ('Settings.View', 'Settings.Manage'));

DELETE FROM public.permissions WHERE code IN ('Settings.View', 'Settings.Manage');

COMMIT;

-- Verification — every role that previously held Settings.View should now hold all three new reads,
-- and no row anywhere should still reference the retired codes.
--
-- SELECT t.subdomain, r.name AS role,
--        bool_or(p.code = 'Areas.View')            AS areas_view,
--        bool_or(p.code = 'Notifications.View')    AS notifications_view,
--        bool_or(p.code = 'BusinessProfile.View')  AS profile_view,
--        bool_or(p.code = 'Areas.Manage')          AS areas_manage
-- FROM public.roles r
-- JOIN public.tenants t ON t.id = r.tenant_id
-- JOIN public.role_permissions rp ON rp.role_id = r.id
-- JOIN public.permissions p ON p.id = rp.permission_id
-- GROUP BY t.subdomain, r.name
-- ORDER BY t.subdomain, r.name;
--
-- SELECT count(*) AS should_be_zero FROM public.permissions
-- WHERE code IN ('Settings.View', 'Settings.Manage');
