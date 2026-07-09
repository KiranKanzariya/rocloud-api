-- Phase 26: platform_users gains refresh-token rotation fields (super-admin portal auth).
-- platform_users is owned by the postgres superuser (created by schema.sql), so run this
-- AS THE DATABASE OWNER (postgres):
--   psql -U postgres -d rocloud_dev -f migration-AddPlatformRefreshToken.sql

ALTER TABLE platform_users
    ADD COLUMN IF NOT EXISTS refresh_token            TEXT,
    ADD COLUMN IF NOT EXISTS refresh_token_expires_at TIMESTAMPTZ;
