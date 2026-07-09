-- ============================================================
-- ROCloud — STEP 5: schema verification (consolidated)
-- Run connected to the rocloud_dev database, in any client.
-- Returns ONE result grid so GUI clients that show only the last
-- result set still display every check.
--
--   psql:  psql -U postgres -d rocloud_dev -f verify.sql
--   GUI:   open while connected to "rocloud_dev", run all
--
-- Every row should read PASS.
-- ============================================================

SELECT check_name, actual, expected,
       CASE WHEN actual = expected THEN 'PASS' ELSE 'FAIL' END AS status
FROM (
    SELECT 'plans_seed (=3)'          AS check_name,
           (SELECT COUNT(*) FROM plans)::int            AS actual, 3  AS expected, 1 AS ord
    UNION ALL
    SELECT 'permissions_seed (=28)',
           (SELECT COUNT(*) FROM permissions)::int,        28, 2
    UNION ALL
    SELECT 'base_tables (=21)',
           (SELECT COUNT(*)::int FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
              AND table_name NOT LIKE 'audit_logs_%'),       21, 3
    UNION ALL
    SELECT 'audit_partitions (=3)',
           (SELECT COUNT(*)::int FROM pg_inherits
            WHERE inhparent = 'audit_logs'::regclass),         3, 4
    UNION ALL
    SELECT 'rls_enabled_tables (=5)',
           (SELECT COUNT(*)::int FROM pg_class
            WHERE relname IN ('customers','orders','deliveries','invoices','payments')
              AND relrowsecurity = true),                      5, 5
    UNION ALL
    SELECT 'rls_tenant_policies (=5)',
           (SELECT COUNT(*)::int FROM pg_policies
            WHERE schemaname = 'public' AND policyname = 'tenant_isolation'), 5, 6
    UNION ALL
    SELECT 'i18n_columns (=3)',
           (SELECT COUNT(*)::int FROM information_schema.columns
            WHERE table_schema = 'public'
              AND ((table_name = 'tenants'   AND column_name = 'default_language')
                OR (table_name = 'users'     AND column_name = 'preferred_language')
                OR (table_name = 'customers' AND column_name = 'preferred_language'))), 3, 7
) t
ORDER BY ord;

-- ------------------------------------------------------------
-- Optional: to eyeball the actual table list, run this separately:
--   SELECT table_name FROM information_schema.tables
--   WHERE table_schema='public' AND table_type='BASE TABLE'
--     AND table_name NOT LIKE 'audit_logs_%' ORDER BY table_name;
-- ------------------------------------------------------------
