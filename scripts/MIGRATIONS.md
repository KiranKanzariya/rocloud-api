# Database migrations & the master script

The database schema is managed by **SQL scripts in this folder**, not EF Core migrations
(the EF migrations drifted long ago). The source of truth is the live database; everything
here exists to *reproduce* and *evolve* it.

Two anchor files:

| File | What it is |
|---|---|
| `master.sql` | **Generated.** The complete current schema (extensions, tables, indexes, constraints, RLS policies, and the structural `audit_logs` partitions). Used to create a brand-new database. Never hand-edited. **No seed data.** |
| `seed-reference-data.sql` | **Hand-maintained.** The shared seed/reference data: plans, permissions, audit-settings, notification templates. Idempotent; run once after `master.sql` on a fresh database. |
| `migration-*.sql` | One incremental change each (a new table, column, index…). Applied in order to existing databases. |

> Scope note: `master.sql` is **schema only**. Seed/reference data lives in
> `seed-reference-data.sql`; the login role and its grants live in `02-create-role.sql` +
> `grant.sql`; the platform admin is created by `create-platform-admin.sql`. This split keeps
> the generated master environment-neutral.

---

## Making a schema change (the routine)

1. **Write a migration** `scripts/migration-<Name>.sql`. Make it idempotent
   (`CREATE TABLE IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`, `ON CONFLICT DO NOTHING`).

2. **Apply it to dev**, as the right role:
   - Plain app-owned tables → the dev role is fine.
   - Tables owned by **postgres** (RLS / partitioned: `customers`, `orders`, `deliveries`,
     `invoices`, `payments`, `audit_logs`) → **run as `postgres`**. `grant.sql`'s
     `ALTER DEFAULT PRIVILEGES` then grants the app role on any new table automatically.
   ```bash
   "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -d rocloud_dev -f scripts/migration-<Name>.sql
   ```

3. **Regenerate the master** so it can't drift — it is re-derived from the live database.
   Run this in **Git Bash** (not PowerShell), from the `rocloud-api` folder:
   ```bash
   PGPASSWORD='PUT_PASSWORD_HERE' bash scripts/regenerate-master.sh
   ```
   (the `Password=…` part of the connection
   string). If that file's password ever changes, use the new value here too.

   You should see it finish with `==> wrote …/master.sql (#### lines)`. If instead you get
   `password authentication failed for user "rocloud_dev_user"`, the password above no longer
   matches — copy the current one from `appsettings.Development.json`.

   Commit the refreshed `scripts/master.sql` alongside your migration.

4. **Apply the same migration to every other database** (prod, Supabase, …) — as `postgres`.
   Do this **before/with** deploying the code that needs it, or the new code hits a missing
   column/table.

5. **Deploy the API** (the usual `app_offline` DLL swap) once all target DBs have the migration.

That's it: **migration → run on dev → `regenerate-master.sh` → commit → run on other DBs → deploy.**
Because the master is generated, it always matches reality — there's nothing to keep in sync by hand.

---

## Creating a fresh database from the master

```bash
createdb rocloud_new
psql -U postgres -d rocloud_new -f scripts/master.sql              # schema + partitions (superuser — CREATE EXTENSION)
psql -U postgres -d rocloud_new -f scripts/02-create-role.sql      # app login role
psql -U postgres -d rocloud_new -f scripts/grant.sql               # grants to the app role
psql -U postgres -d rocloud_new -f scripts/seed-reference-data.sql # plans/permissions/settings/templates
psql -U postgres -d rocloud_new -f scripts/create-platform-admin.sql   # platform admin — edit email/password first
```
> For Hangfire background jobs, also run `hangfire-setup.sql` (dev role) or
> `hangfire-setup-prod.sql` (prod role) as `postgres`.
(Supabase is different — it has no `CREATE DATABASE` and needs a non-owner app role; use
`setup-prod-supabase.sql` instead of `master.sql` there.)

---

## Why it's done this way

- **Postgres owns the RLS/partitioned tables**, so DDL on them needs `postgres`; the app role only
  gets DML via the default-privileges grant. This is why migrations on those tables are a
  "run as postgres" step, not something the app does at startup.
- **The master is generated, not authored**, so it can never disagree with the database. The old
  hand-written `schema.sql` drifted precisely because people forgot to update it — regeneration
  removes that failure mode.
