-- Subscription invoicing (v1): tenant-facing ROCloud subscription invoices (Basic/Pro/Enterprise).
-- Platform-owned table (NOT tenant-RLS), same ownership model as platform_billing_transactions.
-- A row is the owner-facing PAYABLE (Pending) that flips to Paid on payment, or Void when superseded.
-- On payment we ALSO write a platform_billing_transactions row (the admin paid-ledger), so nothing
-- there changes and sums are not double-counted.
-- Run as the database owner (e.g. postgres). Idempotent.

CREATE TABLE IF NOT EXISTS subscription_invoices (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    invoice_number      VARCHAR(30) NOT NULL,
    plan_type           VARCHAR(20) NOT NULL,
    billing_cycle       VARCHAR(10) NOT NULL DEFAULT 'Monthly',   -- Monthly | Yearly
    period_start        DATE NOT NULL,
    period_end          DATE NOT NULL,
    gross_amount        NUMERIC(10,2) NOT NULL,
    discount_amount     NUMERIC(10,2) NOT NULL DEFAULT 0,
    amount              NUMERIC(10,2) NOT NULL,                   -- net payable = gross - discount
    status              VARCHAR(20) NOT NULL DEFAULT 'Pending'
                            CHECK (status IN ('Pending','Paid','Void')),
    due_date            DATE NOT NULL,
    description         VARCHAR(200),
    razorpay_order_id   VARCHAR(100),
    razorpay_payment_id VARCHAR(100),
    paid_at             TIMESTAMPTZ,
    pdf_url             VARCHAR(300),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ
);

-- Human-friendly number is globally unique.
CREATE UNIQUE INDEX IF NOT EXISTS ux_subscription_invoices_number
    ON subscription_invoices(invoice_number);

-- At most one OPEN (Pending) invoice per tenant per period — the generation guard relies on this.
CREATE UNIQUE INDEX IF NOT EXISTS ux_subscription_invoices_open_period
    ON subscription_invoices(tenant_id, period_start)
    WHERE status = 'Pending';

CREATE INDEX IF NOT EXISTS idx_subscription_invoices_tenant ON subscription_invoices(tenant_id);
CREATE INDEX IF NOT EXISTS idx_subscription_invoices_status ON subscription_invoices(status);
