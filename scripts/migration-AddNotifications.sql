-- Phase 24: in-app notification feed (the owner's top-bar bell, guide §24).
-- Creates the notifications table. It references tenants(id) and users(id), which are owned by the
-- postgres superuser, so run this AS THE DATABASE OWNER (postgres). grant.sql's ALTER DEFAULT
-- PRIVILEGES means the app role (rocloud_dev_user) is granted on it automatically:
--   psql -U postgres -d rocloud_dev -f migration-AddNotifications.sql

CREATE TABLE IF NOT EXISTS notifications (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    user_id         UUID NOT NULL REFERENCES users(id)   ON DELETE CASCADE,
    type            VARCHAR(40)  NOT NULL,
                        -- InvoicesOverdue | OrdersPending | AmcDue | ServiceOpen
    title           VARCHAR(200) NOT NULL,
    link            VARCHAR(200),
    reference_key   VARCHAR(120) NOT NULL,
    is_read         BOOLEAN NOT NULL DEFAULT false,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ,
    UNIQUE (tenant_id, user_id, type)
);

CREATE INDEX IF NOT EXISTS idx_notifications_unread ON notifications(tenant_id, user_id, is_read);

-- If your install does NOT use grant.sql's ALTER DEFAULT PRIVILEGES, grant explicitly:
--   GRANT ALL PRIVILEGES ON notifications TO rocloud_dev_user;  -- dev
--   GRANT ALL PRIVILEGES ON notifications TO rocloud_app;       -- prod
