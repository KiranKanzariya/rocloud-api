-- ============================================================================
-- Reset a TENANT user's password (owner-portal login, table: users).
--
-- Passwords are BCrypt hashes (one-way) — the original cannot be recovered, only
-- RESET to a new known value. This sets a new password via pgcrypto's
-- crypt(... gen_salt('bf', 12)) — a $2a$/cost-12 hash that the app's BCrypt.Net
-- login verifies (same as PasswordService, work factor 12).
--
-- Run as the postgres superuser (users is postgres-owned, CREATE EXTENSION needs
-- superuser). Works in pgAdmin / DBeaver / psql (no \set meta-commands):
--   "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -d rocloud_dev -f scripts\reset-user-password.sql
-- ============================================================================

-- STEP 1 — find the user. email is unique only PER TENANT, so note the subdomain.
SELECT u.id, u.name, u.email, t.subdomain AS tenant, u.is_active, u.auth_provider
FROM   users u
JOIN   tenants t ON t.id = u.tenant_id
WHERE  u.is_deleted = false
ORDER  BY t.subdomain, u.name;

-- STEP 2 — reset the password. EDIT the email, the new password, and (if the same
-- email exists in more than one tenant) uncomment the subdomain filter.
CREATE EXTENSION IF NOT EXISTS pgcrypto;

UPDATE users u
   SET password_hash = crypt('NewPassw0rd!', gen_salt('bf', 12)),   -- <-- new password
       auth_provider = CASE WHEN u.auth_provider = 'google' THEN 'both' ELSE 'custom' END,
       is_active     = true,
       updated_at    = NOW()
 WHERE lower(u.email) = lower('owner@example.com')                   -- <-- target user email
   AND u.is_deleted = false
   -- AND u.tenant_id = (SELECT id FROM tenants WHERE subdomain = 'sharma')   -- <-- if email is not unique
RETURNING u.id, u.email, u.tenant_id, u.is_active, u.auth_provider;
