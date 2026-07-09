-- ════════════════════════════════════════════════════════════════════════════
-- Customer mobile normalisation — pgAdmin version (pure SQL, no psql meta-commands).
--
-- Canonicalises legacy customer mobiles to the app's "+91XXXXXXXXXX" form. Collision-safe
-- and per-tenant: never produces two customers sharing a canonical number within a tenant,
-- never invents a number from junk.
--
-- ⚠ customers has RLS — CONNECT AS THE postgres SUPERUSER in pgAdmin (it must see every
--    tenant). A normal app-role connection will see no rows.
--
-- HOW TO USE:
--   STEP 1 — select the STEP 1 block below and Execute (F5). Review the plan grid.
--   STEP 2 — only if the plan looks right, select the STEP 2 block and Execute.
--
-- Actions in the plan:
--   OK        already canonical                              → untouched
--   UPDATE    non-canonical, valid Indian mobile (6-9 start),→ mobile rewritten to +91…
--             no collision
--   COLLISION canonical value already used by / shared with  → untouched (resolve by hand)
--             another customer in the tenant
--   INVALID   can't derive a valid 10-digit 6-9 mobile       → untouched (fix by hand)
-- ════════════════════════════════════════════════════════════════════════════


-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 1 — DRY RUN (read-only). Select from here to the end of the SELECT, Execute.
-- ─────────────────────────────────────────────────────────────────────────────
WITH base AS (
    SELECT c.id, c.tenant_id, c.name, c.mobile, c.is_deleted,
           regexp_replace(coalesce(c.mobile, ''), '\D', '', 'g') AS digits
    FROM customers c
),
cored AS (
    SELECT *, CASE
               WHEN length(digits) = 12 AND left(digits, 2) = '91' THEN right(digits, 10)
               WHEN length(digits) = 11 AND left(digits, 1) = '0'  THEN right(digits, 10)
               WHEN length(digits) = 10                            THEN digits
               ELSE NULL
           END AS core
    FROM base
),
classified AS (
    SELECT *,
           (mobile ~ '^\+91[0-9]{10}$')                      AS already_canonical,
           CASE WHEN core IS NOT NULL THEN '+91' || core END AS canonical,
           (core ~ '^[6-9][0-9]{9}$')                        AS valid_mobile
    FROM cored
),
flagged AS (
    SELECT *, (NOT already_canonical AND core IS NOT NULL AND valid_mobile) AS is_candidate
    FROM classified
),
targets AS (
    SELECT tenant_id, canonical,
           count(*) FILTER (WHERE already_canonical) AS existing_holders,
           count(*) FILTER (WHERE is_candidate)      AS candidate_count
    FROM flagged WHERE canonical IS NOT NULL
    GROUP BY tenant_id, canonical
),
plan AS (
    SELECT f.id, f.tenant_id, f.name, f.mobile AS current_mobile, f.is_deleted, f.canonical,
           CASE
               WHEN f.already_canonical                             THEN 'OK'
               WHEN f.core IS NULL OR NOT f.valid_mobile            THEN 'INVALID'
               WHEN t.existing_holders > 0 OR t.candidate_count > 1 THEN 'COLLISION'
               ELSE 'UPDATE'
           END AS action
    FROM flagged f
    LEFT JOIN targets t ON t.tenant_id = f.tenant_id AND t.canonical = f.canonical
)
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
FROM plan p
ORDER BY (p.action = 'OK'), tenant, p.action, p.name;   -- attention rows first, OK rows last


-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 2 — COMMIT. Run ONLY after reviewing STEP 1. Select this whole block, Execute.
-- It rewrites mobile for the UPDATE rows only. Result message shows "UPDATE <n>".
--
-- Safety option: run "BEGIN;" first, then this UPDATE, check the count, then run
-- "COMMIT;" (or "ROLLBACK;" to undo).
-- ─────────────────────────────────────────────────────────────────────────────
WITH base AS (
    SELECT c.id, c.tenant_id, c.mobile,
           regexp_replace(coalesce(c.mobile, ''), '\D', '', 'g') AS digits
    FROM customers c
),
cored AS (
    SELECT *, CASE
               WHEN length(digits) = 12 AND left(digits, 2) = '91' THEN right(digits, 10)
               WHEN length(digits) = 11 AND left(digits, 1) = '0'  THEN right(digits, 10)
               WHEN length(digits) = 10                            THEN digits
               ELSE NULL
           END AS core
    FROM base
),
classified AS (
    SELECT *,
           (mobile ~ '^\+91[0-9]{10}$')                      AS already_canonical,
           CASE WHEN core IS NOT NULL THEN '+91' || core END AS canonical,
           (core ~ '^[6-9][0-9]{9}$')                        AS valid_mobile
    FROM cored
),
flagged AS (
    SELECT *, (NOT already_canonical AND core IS NOT NULL AND valid_mobile) AS is_candidate
    FROM classified
),
targets AS (
    SELECT tenant_id, canonical,
           count(*) FILTER (WHERE already_canonical) AS existing_holders,
           count(*) FILTER (WHERE is_candidate)      AS candidate_count
    FROM flagged WHERE canonical IS NOT NULL
    GROUP BY tenant_id, canonical
),
to_update AS (
    SELECT f.id, f.canonical
    FROM flagged f
    LEFT JOIN targets t ON t.tenant_id = f.tenant_id AND t.canonical = f.canonical
    WHERE f.is_candidate
      AND COALESCE(t.existing_holders, 0) = 0
      AND COALESCE(t.candidate_count, 0) = 1
)
UPDATE customers c
SET mobile = u.canonical, updated_at = now()
FROM to_update u
WHERE c.id = u.id;
