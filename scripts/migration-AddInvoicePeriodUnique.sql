-- ─────────────────────────────────────────────────────────────────────────────
-- Migration: unique partial index guarding against duplicate period invoices
-- ─────────────────────────────────────────────────────────────────────────────
-- Enforces at most one live (non-cancelled, non-deleted) invoice per
-- (tenant_id, customer_id, period_from, period_to). This is the hard backstop behind the
-- BulkGenerateInvoices idempotency guard, so even a concurrent double-run cannot create a duplicate
-- monthly invoice. Ad-hoc invoices (period_from IS NULL) are not constrained.
--
-- ⚠️ If the table ALREADY contains duplicate period invoices (from earlier double-runs), CREATE
--    UNIQUE INDEX will FAIL. Find and resolve them first, e.g.:
--      SELECT tenant_id, customer_id, period_from, period_to, count(*)
--      FROM public.invoices
--      WHERE status <> 'Cancelled' AND is_deleted = false AND period_from IS NOT NULL
--      GROUP BY 1,2,3,4 HAVING count(*) > 1;
--    Cancel or soft-delete the extras (set status='Cancelled' or is_deleted=true), then re-run this.
--
-- Idempotent (IF NOT EXISTS). NOTE: `invoices` is a postgres-owned (RLS) table, so run this as the
-- `postgres` role, not the app role (CREATE INDEX requires table ownership). See scripts/MIGRATIONS.md.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE UNIQUE INDEX IF NOT EXISTS ux_invoices_customer_period
    ON public.invoices USING btree (tenant_id, customer_id, period_from, period_to)
    WHERE (((status)::text <> 'Cancelled'::text) AND (is_deleted = false) AND (period_from IS NOT NULL));
