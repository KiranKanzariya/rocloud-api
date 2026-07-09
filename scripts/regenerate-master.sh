#!/usr/bin/env bash
# ============================================================================
# Regenerate scripts/master.sql — the canonical, full database schema + seed.
#
# WHY: the database is the source of truth (schema.sql had drifted). Rather than
# hand-maintaining a master script, this DERIVES it from the live database, so it
# is always current. Run this after ANY schema change (a migration applied to dev)
# and commit the refreshed master.sql.
#
#   PGPASSWORD='<dev password>' bash scripts/regenerate-master.sh
#
# It dumps the live schema (no ownership, no Hangfire, and WITHOUT the dated
# audit_logs partitions — those are recreated dynamically so the master never goes
# stale), and appends a footer that creates the current + next two audit_logs
# partitions for whatever month the master is run.
#
# Seed / reference data (plans, permissions, audit-settings, notification templates)
# is NO LONGER part of master.sql — it lives in the hand-maintained
# scripts/seed-reference-data.sql. Run that after master.sql on a fresh database.
# (The platform admin is separate again: scripts/create-platform-admin.sql.)
# ============================================================================
set -euo pipefail

PGBIN="${PGBIN:-/c/Program Files/PostgreSQL/18/bin}"
PGHOST="${PGHOST:-localhost}"
SRCDB="${SRCDB:-rocloud_dev}"
SRCUSER="${SRCUSER:-rocloud_dev_user}"
PGDUMP="$PGBIN/pg_dump.exe"

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/master.sql"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

echo "==> dumping schema from $SRCDB"
"$PGDUMP" -U "$SRCUSER" -h "$PGHOST" -d "$SRCDB" --schema-only --no-owner --no-privileges \
  --no-tablespaces --exclude-schema=hangfire --exclude-table='public.audit_logs_*' \
  | sed '/^[\]/d' > "$TMP/schema.sql"

echo "==> assembling master.sql"
{
  cat <<'HEADER'
-- ════════════════════════════════════════════════════════════════════════════
-- master.sql — ROCloud canonical database SCHEMA.  *** GENERATED ***
--
-- Do NOT hand-edit. Regenerate from the live database after any change:
--     PGPASSWORD='<dev password>' bash scripts/regenerate-master.sh
--
-- Create a fresh database from this file (run as a superuser — CREATE EXTENSION):
--     createdb rocloud_new
--     psql -U postgres -d rocloud_new -f scripts/master.sql
-- Then grant the app role (scripts/02-create-role.sql + grant.sql), load the
-- seed/reference data (scripts/seed-reference-data.sql), and create a platform
-- admin (scripts/create-platform-admin.sql).
--
-- Includes: extensions, all tables/indexes/constraints, RLS policies, and the
-- partitioned audit_logs (current + next 2 monthly partitions).
-- Seed/reference data is NOT here — see scripts/seed-reference-data.sql.
-- ════════════════════════════════════════════════════════════════════════════

HEADER

  cat "$TMP/schema.sql"

  cat <<'FOOTER'


-- ─────────────────────────────────────────────────────────────────────────────
-- SEED / REFERENCE DATA is NOT in this file — see scripts/seed-reference-data.sql
-- (plans, permissions, audit_settings, notification templates) and
-- scripts/create-platform-admin.sql (the platform admin). Only the structural
-- audit_logs partitions (needed before any audit row can be inserted) are below.
-- ─────────────────────────────────────────────────────────────────────────────
-- audit_logs partitions: create the current month + the next two, for whenever
-- this master is run (the audit-log-partition job keeps making future ones).
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE
    m date := date_trunc('month', now())::date;
    i int;
BEGIN
    FOR i IN 0..2 LOOP
        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS public.audit_logs_%s PARTITION OF public.audit_logs FOR VALUES FROM (%L) TO (%L)',
            to_char(m + (i || ' month')::interval, 'YYYY_MM'),
            (m + (i || ' month')::interval),
            (m + ((i + 1) || ' month')::interval));
    END LOOP;
END
$$;
FOOTER
} > "$OUT"

echo "==> wrote $OUT ($(wc -l < "$OUT") lines)"
