-- Phase 26: cross-tenant support tickets handled by the platform team (super-admin portal).
-- Run as the database owner (e.g. postgres).

CREATE TABLE IF NOT EXISTS support_tickets (
    id                        UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                 UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    subject                   VARCHAR(200) NOT NULL,
    description               TEXT,
    status                    VARCHAR(20) NOT NULL DEFAULT 'Open'
                                  CHECK (status IN ('Open','InProgress','Resolved','Closed')),
    priority                  VARCHAR(20) NOT NULL DEFAULT 'Medium'
                                  CHECK (priority IN ('Low','Medium','High','Urgent')),
    assigned_platform_user_id UUID REFERENCES platform_users(id) ON DELETE SET NULL,
    resolution_note           TEXT,
    created_at                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at                TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_support_tickets_status ON support_tickets(status);
CREATE INDEX IF NOT EXISTS idx_support_tickets_tenant ON support_tickets(tenant_id);
