-- ────────────────────────────────────────────────────────────────────────────
-- Customer mobile normalisation — one-off data cleanup.
--
-- Canonicalises legacy customer mobiles to the app's "+91XXXXXXXXXX" form so the
-- duplicate-mobile guards (import + manual create) work everywhere. Collision-safe:
-- it never produces two customers sharing the same canonical number within a tenant,
-- and never invents a number out of junk. Per-tenant: collisions are scoped by tenant_id.
--
-- customers has RLS, so RUN THIS AS THE postgres SUPERUSER (it must see every tenant):
--
--   Dry run (review only, NO writes):
--     psql -U postgres -d rocloud_dev -f normalize-customer-mobiles.sql
--
--   Commit (apply the UPDATE rows):
--     psql -U postgres -d rocloud_dev -f normalize-customer-mobiles.sql -v commit=true
--
-- Row classification:
--   OK        already canonical (^\+91[0-9]{10}$)            → untouched
--   UPDATE    non-canonical, resolves to a valid Indian       → mobile rewritten to +91…
--             mobile (10 digits starting 6-9), no collision
--   COLLISION its canonical value is already used by, or      → untouched (resolve by hand)
--             shared with, another customer in the tenant
--   INVALID   can't derive a valid 10-digit 6-9 mobile        → untouched (fix by hand)
-- ────────────────────────────────────────────────────────────────────────────

-- Default the commit flag to false unless the caller passed -v commit=true.
\if :{?commit}
\else
  \set commit false
\endif

DROP TABLE IF EXISTS _mobile_plan;

CREATE TEMP TABLE _mobile_plan AS
WITH base AS (
    SELECT c.id, c.tenant_id, c.name, c.mobile, c.is_deleted,
           regexp_replace(coalesce(c.mobile, ''), '\D', '', 'g') AS digits
    FROM customers c
),
cored AS (
    SELECT *,
           CASE
               WHEN length(digits) = 12 AND left(digits, 2) = '91' THEN right(digits, 10)
               WHEN length(digits) = 11 AND left(digits, 1) = '0'  THEN right(digits, 10)
               WHEN length(digits) = 10                            THEN digits
               ELSE NULL
           END AS core
    FROM base
),
classified AS (
    SELECT *,
           (mobile ~ '^\+91[0-9]{10}$')                  AS already_canonical,
           CASE WHEN core IS NOT NULL THEN '+91' || core END AS canonical,
           (core ~ '^[6-9][0-9]{9}$')                    AS valid_mobile
    FROM cored
),
flagged AS (
    SELECT *,
           (NOT already_canonical AND core IS NOT NULL AND valid_mobile) AS is_candidate
    FROM classified
),
-- Per tenant + target number: who already holds it, and how many rows want to become it.
targets AS (
    SELECT tenant_id, canonical,
           count(*) FILTER (WHERE already_canonical) AS existing_holders,
           count(*) FILTER (WHERE is_candidate)      AS candidate_count
    FROM flagged
    WHERE canonical IS NOT NULL
    GROUP BY tenant_id, canonical
)
SELECT f.id, f.tenant_id, f.name, f.mobile AS current_mobile, f.is_deleted,
       f.canonical,
       CASE
           WHEN f.already_canonical                              THEN 'OK'
           WHEN f.core IS NULL OR NOT f.valid_mobile             THEN 'INVALID'
           WHEN t.existing_holders > 0 OR t.candidate_count > 1  THEN 'COLLISION'
           ELSE 'UPDATE'
       END AS action
FROM flagged f
LEFT JOIN targets t ON t.tenant_id = f.tenant_id AND t.canonical = f.canonical;

\echo ''
\echo '==== Summary (rows by action) ===================================='
SELECT action, count(*) AS rows
FROM _mobile_plan
GROUP BY action
ORDER BY action;

\echo ''
\echo '==== Detail (every row that is not already canonical) ============'
SELECT
    (SELECT subdomain FROM tenants WHERE id = p.tenant_id) AS tenant,
    p.action,
    p.name,
    p.current_mobile,
    CASE WHEN p.action = 'UPDATE' THEN p.canonical END AS new_mobile,
    p.is_deleted,
    CASE
        WHEN p.action = 'INVALID'   THEN 'No valid 10-digit mobile (must start 6-9)'
        WHEN p.action = 'COLLISION' THEN 'Canonical number already used by / shared with another customer'
    END AS note
FROM _mobile_plan p
WHERE p.action <> 'OK'
ORDER BY tenant, p.action, p.name;

\if :commit
    \echo ''
    \echo '==== COMMIT: applying UPDATE rows ================================'
    UPDATE customers c
    SET mobile = p.canonical, updated_at = now()
    FROM _mobile_plan p
    WHERE p.id = c.id AND p.action = 'UPDATE';
    \echo 'Committed. Rows updated:'
    SELECT count(*) AS updated FROM _mobile_plan WHERE action = 'UPDATE';
\else
    \echo ''
    \echo '==== DRY RUN — nothing written. ================================='
    \echo 'Review the plan above, then re-run with  -v commit=true  to apply UPDATE rows.'
\endif

DROP TABLE IF EXISTS _mobile_plan;
