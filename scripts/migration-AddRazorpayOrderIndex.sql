-- Maps a Razorpay order id → tenant + local payment so the anonymous webhook can resolve the
-- tenant BEFORE reading the RLS-protected payments row (the webhook has no tenant context, and
-- RLS otherwise returns zero rows). Deliberately NOT RLS-protected and NOT tenant-scoped.
--
-- Run AS THE DATABASE OWNER (postgres). grant.sql's ALTER DEFAULT PRIVILEGES grants the app role.
-- Apply to BOTH dev and prod.

CREATE TABLE IF NOT EXISTS razorpay_order_index (
    razorpay_order_id VARCHAR(64) PRIMARY KEY,
    tenant_id         UUID        NOT NULL,
    payment_id        UUID        NOT NULL,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
