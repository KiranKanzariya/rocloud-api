-- ─────────────────────────────────────────────────────────────────────────────
-- Read-only audit: duplicate opening-balance rows
-- ─────────────────────────────────────────────────────────────────────────────
-- Finds customers left with MORE THAN ONE opening balance from the earlier failed/retried imports
-- (before the count-based invoice-numbering bug was fixed). SELECT-only — changes nothing.
--
-- An opening balance should create, per customer, AT MOST: one [opening-balance] invoice (dues > 0)
-- OR one [opening-balance] advance payment (dues < 0), plus one Issue movement PER PRODUCT.
-- So >1 invoice, >1 payment, >1 movement-per-product, or BOTH an invoice and a payment = a duplicate.
--
-- Run as the `postgres`/owner role (invoices & payments are RLS tables). To scope to one tenant, add
--   AND tenant_id = '<tenant-uuid>'
-- to each WHERE. Marker is '[opening-balance]' (see SetCustomerOpeningBalanceCommand.Marker).
-- ─────────────────────────────────────────────────────────────────────────────

\echo '== 1) Customers with MORE THAN ONE live opening-balance invoice =='
SELECT tenant_id,
       customer_id,
       count(*) AS opening_invoices,
       string_agg(invoice_number || ' [' || status || ' ' || total_amount || ']', ', '
                  ORDER BY invoice_number) AS invoices
FROM public.invoices
WHERE notes LIKE '[opening-balance]%'
  AND is_deleted = false
  AND status <> 'Cancelled'
GROUP BY tenant_id, customer_id
HAVING count(*) > 1
ORDER BY count(*) DESC, tenant_id;

\echo '== 2) Customers with MORE THAN ONE opening-balance advance payment =='
SELECT tenant_id,
       customer_id,
       count(*)     AS opening_payments,
       sum(amount)  AS total_advance
FROM public.payments
WHERE notes LIKE '[opening-balance]%'
  AND status = 'Completed'
GROUP BY tenant_id, customer_id
HAVING count(*) > 1
ORDER BY count(*) DESC, tenant_id;

\echo '== 3) Duplicate opening-balance jar movements (same customer + product more than once) =='
SELECT tenant_id,
       customer_id,
       product_id,
       count(*)       AS issue_movements,
       sum(quantity)  AS total_qty
FROM public.inventory_movements
WHERE notes LIKE '[opening-balance]%'
  AND movement_type = 'Issue'
GROUP BY tenant_id, customer_id, product_id
HAVING count(*) > 1
ORDER BY count(*) DESC, tenant_id;

\echo '== 4) Contradictory: a customer with BOTH an opening-balance invoice AND advance payment =='
SELECT i.tenant_id, i.customer_id
FROM (SELECT DISTINCT tenant_id, customer_id FROM public.invoices
      WHERE notes LIKE '[opening-balance]%' AND is_deleted = false AND status <> 'Cancelled') i
JOIN (SELECT DISTINCT tenant_id, customer_id FROM public.payments
      WHERE notes LIKE '[opening-balance]%' AND status = 'Completed') p
  ON i.tenant_id = p.tenant_id AND i.customer_id = p.customer_id
ORDER BY i.tenant_id;

\echo '== 5) Per-tenant summary (how widespread) =='
SELECT tenant_id,
       count(*)                        AS opening_invoices_total,
       count(DISTINCT customer_id)     AS customers_with_opening_invoice,
       count(*) - count(DISTINCT customer_id) AS extra_duplicate_invoices
FROM public.invoices
WHERE notes LIKE '[opening-balance]%'
  AND is_deleted = false
  AND status <> 'Cancelled'
GROUP BY tenant_id
ORDER BY extra_duplicate_invoices DESC;

-- To inspect one flagged customer in full (replace the uuid):
--   SELECT invoice_number, status, total_amount, invoice_date, created_at, notes
--   FROM public.invoices
--   WHERE customer_id = '00000000-0000-0000-0000-000000000000' AND notes LIKE '[opening-balance]%'
--   ORDER BY created_at;
