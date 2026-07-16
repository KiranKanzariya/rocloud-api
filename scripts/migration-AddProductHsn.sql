-- ─────────────────────────────────────────────────────────────────────────────
-- Migration: per-product HSN / SAC code for GST tax invoices
-- ─────────────────────────────────────────────────────────────────────────────
-- Adds products.hsn. The invoice PDF prints this per line item on a Tax Invoice (and falls back to
-- '2201' — packaged drinking water — when null). Backfills existing rows to 2201 since the current
-- catalogue is water only; future non-water goods/services can be given their own HSN/SAC.
--
-- `products` is an app-owned table (no RLS), so the app role may run this.
-- ⚠️ Run this BEFORE deploying the matching API build: once the code ships, EF selects the `hsn`
--    column on every product query, so the column must already exist.
-- Idempotent (IF NOT EXISTS + null-guarded backfill).
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE public.products ADD COLUMN IF NOT EXISTS hsn character varying(8);

UPDATE public.products SET hsn = '2201' WHERE hsn IS NULL;   -- packaged drinking water
