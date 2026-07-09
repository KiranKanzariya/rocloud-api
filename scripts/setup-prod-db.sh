#!/usr/bin/env bash
# ============================================================================
# Create a SEPARATE production database with the CURRENT schema.
#
# WHY a clone (not "run schema.sql")? The hand-written schema.sql has drifted —
# it's missing later migrations (discount, status_code, support_tickets,
# platform_billing, subscription_discount, mobile-optional, …). The only
# reliable way to make prod === the live schema is to clone the live structure.
# This script copies STRUCTURE + reference data (plans, permissions) only —
# NOT any tenant data (customers/orders/etc. stay in dev).
#
# Run in Git Bash on the DB host, by someone who can authenticate as the
# postgres superuser:
#     PROD_PASSWORD='a-strong-prod-password' bash scripts/setup-prod-db.sh
#
# Re-runnable: role/db are created only if missing; the schema clone assumes a
# FRESH prod db (drop it first to re-clone:  DROP DATABASE rocloud_prod;).
# ============================================================================
set -euo pipefail

# ---- config (override via env) --------------------------------------------
PGBIN="${PGBIN:-/c/Program Files/PostgreSQL/18/bin}"
PGHOST="${PGHOST:-localhost}"
SUPER="${SUPER:-postgres}"
SOURCE_DB="${SOURCE_DB:-rocloud_dev}"
PROD_DB="${PROD_DB:-rocloud_prod}"
PROD_ROLE="${PROD_ROLE:-rocloud_app}"
PROD_PASSWORD="${PROD_PASSWORD:-}"            # the new prod login role's password
PSQL="$PGBIN/psql.exe"
PGDUMP="$PGBIN/pg_dump.exe"

if [ -z "$PROD_PASSWORD" ]; then
  echo "ERROR: set PROD_PASSWORD (the password for the new '${PROD_ROLE}' login role)." >&2
  echo "       e.g.  PROD_PASSWORD='S0me-Strong-Pass' bash scripts/setup-prod-db.sh" >&2
  exit 1
fi

# One prompt for the postgres password; reused by every connection below (all use -U postgres).
read -rsp "postgres superuser password: " PGPASSWORD; echo
export PGPASSWORD

psuper() { "$PSQL" -U "$SUPER" -h "$PGHOST" -v ON_ERROR_STOP=1 "$@"; }

echo "==> 1/8  Create prod login role '${PROD_ROLE}' (if missing)"
psuper -d postgres -c "DO \$\$ BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='${PROD_ROLE}') THEN
    CREATE ROLE ${PROD_ROLE} LOGIN PASSWORD '${PROD_PASSWORD}';
  END IF;
END \$\$;"

echo "==> 2/8  Create database '${PROD_DB}' (if missing)"
if ! psuper -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='${PROD_DB}'" | grep -q 1; then
  # CREATE DATABASE cannot run in a transaction block — issue it on its own.
  psuper -d postgres -c "CREATE DATABASE ${PROD_DB};"
else
  echo "    (already exists — schema clone below assumes it is empty/fresh)"
fi
psuper -d postgres -c "GRANT CONNECT ON DATABASE ${PROD_DB} TO ${PROD_ROLE};"

echo "==> 3/8  Clone schema (tables, indexes, RLS, partitions, extensions) from '${SOURCE_DB}'"
# --no-owner/--no-privileges: every object ends up owned by postgres, so the app
#   role is subject to RLS (postgres owner bypasses it; the app never should).
# --exclude-schema=hangfire: the API rebuilds its own Hangfire tables on startup.
"$PGDUMP" -U "$SUPER" -h "$PGHOST" -d "$SOURCE_DB" \
  --schema-only --no-owner --no-privileges --exclude-schema=hangfire \
  | psuper -d "$PROD_DB"

echo "==> 4/8  Seed reference data (plans + permissions only — NOT tenant data)"
"$PGDUMP" -U "$SUPER" -h "$PGHOST" -d "$SOURCE_DB" \
  --data-only --table=public.plans --table=public.permissions \
  | psuper -d "$PROD_DB"

echo "==> 5/8  Grant privileges to '${PROD_ROLE}' on public schema"
psuper -d "$PROD_DB" <<SQL
GRANT USAGE, CREATE ON SCHEMA public TO ${PROD_ROLE};
GRANT ALL PRIVILEGES ON ALL TABLES    IN SCHEMA public TO ${PROD_ROLE};
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO ${PROD_ROLE};
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL PRIVILEGES ON TABLES    TO ${PROD_ROLE};
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL PRIVILEGES ON SEQUENCES TO ${PROD_ROLE};
SQL

echo "==> 6/8  Prepare Hangfire schema (app installs its tables on first run)"
psuper -d "$PROD_DB" <<SQL
CREATE SCHEMA IF NOT EXISTS hangfire;
GRANT ALL ON SCHEMA hangfire TO ${PROD_ROLE};
ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON TABLES TO ${PROD_ROLE};
ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON SEQUENCES TO ${PROD_ROLE};
SQL

echo "==> 7/8  Lock down audit_logs (append-only — no UPDATE/DELETE for the app)"
psuper -d "$PROD_DB" <<SQL
REVOKE UPDATE, DELETE, TRUNCATE ON audit_logs FROM ${PROD_ROLE};
GRANT  INSERT, SELECT             ON audit_logs TO   ${PROD_ROLE};
SQL

echo "==> 8/8  Verify"
psuper -d "$PROD_DB" -c "SELECT
  (SELECT count(*) FROM information_schema.tables WHERE table_schema='public') AS tables,
  (SELECT count(*) FROM pg_policies)                                          AS rls_policies,
  (SELECT count(*) FROM plans)                                                AS plans,
  (SELECT count(*) FROM permissions)                                          AS permissions;"

cat <<DONE

Done. '${PROD_DB}' is ready with the current schema, RLS, and reference data.

NEXT (manual):
  1. Create a platform admin in prod (edit email/password inside the file first):
       "$PSQL" -U ${SUPER} -h ${PGHOST} -d ${PROD_DB} -f scripts/create-platform-admin.sql
  2. Point production at the new DB — in C:\\inetpub\\rocloud-api\\web.config set:
       ConnectionStrings__Default =
         Host=${PGHOST};Port=5432;Database=${PROD_DB};Username=${PROD_ROLE};Password=${PROD_PASSWORD};
  3. Recycle the IIS app pool (or app_offline dance) so the API reconnects.
  4. Tenants/customers do NOT carry over — production starts empty (register fresh,
     or migrate specific tenants deliberately). Dev keeps using '${SOURCE_DB}'.

From now on, run future migration-*.sql scripts against BOTH databases (as postgres).
DONE
