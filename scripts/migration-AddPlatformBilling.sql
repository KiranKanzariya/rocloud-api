-- Phase 26: platform billing transactions (tenant subscription charges, super-admin portal).
-- Run as the database owner (e.g. postgres).

CREATE TABLE IF NOT EXISTS platform_billing_transactions (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    plan_type           VARCHAR(20) NOT NULL,
    amount              NUMERIC(10,2) NOT NULL,
    billing_cycle       VARCHAR(10) NOT NULL DEFAULT 'Monthly',
    status              VARCHAR(20) NOT NULL DEFAULT 'Paid'
                            CHECK (status IN ('Paid','Failed','Refunded','Pending')),
    razorpay_payment_id VARCHAR(100),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_platform_billing_tenant ON platform_billing_transactions(tenant_id);
CREATE INDEX IF NOT EXISTS idx_platform_billing_status ON platform_billing_transactions(status);
