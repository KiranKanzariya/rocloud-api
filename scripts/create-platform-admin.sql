-- ============================================================================
-- Create (or reset) a ROCloud platform staff user for the SUPER-ADMIN portal.
--
-- Run as the postgres superuser (platform_users is postgres-owned, and
-- CREATE EXTENSION needs superuser). Works in ANY client — pgAdmin, DBeaver,
-- or psql (no \set meta-commands).
--
-- The password is BCrypt-hashed via pgcrypto's crypt(... gen_salt('bf', 12)).
-- That produces a standard $2a$ / cost-12 hash, identical in format to the app's
-- PasswordService (BCrypt.Net, work factor 12), so the portal login verifies it.
--
-- Idempotent: re-running with the same email updates the password / role and
-- re-activates the account (ON CONFLICT (email)).
--
-- EDIT the four literals below, then run.
-- platform_role must be one of: SuperAdmin | Support | Finance
-- ============================================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto;

INSERT INTO platform_users (name, email, password_hash, platform_role, is_active)
VALUES (
    'Super Admin',                                   -- name
    lower('admin@rocloud.app'),                      -- email (lowercased)
    crypt('SuperSecret99', gen_salt('bf', 12)),      -- password -> bcrypt hash
    'SuperAdmin',                                     -- platform_role
    true
)
ON CONFLICT (email) DO UPDATE
   SET password_hash = EXCLUDED.password_hash,
       platform_role = EXCLUDED.platform_role,
       name          = EXCLUDED.name,
       is_active     = true,
       updated_at    = NOW()
RETURNING id, email, platform_role, is_active;
