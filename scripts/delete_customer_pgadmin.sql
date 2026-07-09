-- ════════════════════════════════════════════════════════════════════════════
-- HARD-DELETE a single customer and ALL related data — pgAdmin version.
--
-- Permanently removes the customer plus every dependent row (orders, order_items,
-- deliveries, delivery_items, payments, invoices, inventory_movements,
-- customer_subscriptions, service_requests, amc_subscriptions). IRREVERSIBLE.
--
-- ⚠ customers and several child tables have RLS — CONNECT AS THE postgres SUPERUSER
--    in pgAdmin, otherwise it sees/deletes nothing.
--
-- HOW TO USE:
--   1. STEP A — find the customer id (edit the WHERE, Execute, copy the id).
--   2. STEP B — paste the id into v_id, leave v_dry := true, Execute. Read the
--      row counts in the Messages tab.
--   3. If the counts look right, set v_dry := false and Execute again to delete.
-- ════════════════════════════════════════════════════════════════════════════


-- ─────────────────────────────────────────────────────────────────────────────
-- STEP A — find the customer id (read-only). Edit the filter to taste.
-- ─────────────────────────────────────────────────────────────────────────────
SELECT c.id, c.name, c.mobile, c.is_deleted,
       (SELECT subdomain FROM tenants t WHERE t.id = c.tenant_id) AS tenant
FROM customers c
WHERE c.mobile ILIKE '%9876543210%'      -- ← change: search by mobile…
   OR c.name   ILIKE '%ravi%'            -- ← …or by name
ORDER BY tenant, c.name;


-- ─────────────────────────────────────────────────────────────────────────────
-- STEP B — preview (v_dry := true) or delete (v_dry := false). Edit v_id first.
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE
    v_id   uuid := '00000000-0000-0000-0000-000000000000';  -- ← PUT THE CUSTOMER ID HERE
    v_dry  boolean := true;                                  -- ← true = preview only; false = DELETE

    v_oids uuid[];
    v_iids uuid[];
    v_name text;
    n      int;
BEGIN
    SELECT name INTO v_name FROM customers WHERE id = v_id;
    IF v_name IS NULL THEN
        RAISE NOTICE 'No customer with id % — nothing to do.', v_id;
        RETURN;
    END IF;

    v_oids := COALESCE((SELECT array_agg(id) FROM orders   WHERE customer_id = v_id), '{}');
    v_iids := COALESCE((SELECT array_agg(id) FROM invoices WHERE customer_id = v_id), '{}');

    RAISE NOTICE 'Customer % (%) — related rows:', v_name, v_id;
    SELECT count(*) INTO n FROM order_items   WHERE order_id   = ANY(v_oids);                    RAISE NOTICE '  order_items            : %', n;
    SELECT count(*) INTO n FROM delivery_items WHERE delivery_id IN (SELECT id FROM deliveries WHERE order_id = ANY(v_oids))
                                                  OR order_item_id IN (SELECT id FROM order_items WHERE order_id = ANY(v_oids)); RAISE NOTICE '  delivery_items         : %', n;
    SELECT count(*) INTO n FROM deliveries    WHERE order_id   = ANY(v_oids);                    RAISE NOTICE '  deliveries             : %', n;
    SELECT count(*) INTO n FROM payments      WHERE customer_id = v_id OR order_id = ANY(v_oids) OR invoice_id = ANY(v_iids); RAISE NOTICE '  payments               : %', n;
    SELECT count(*) INTO n FROM inventory_movements WHERE customer_id = v_id OR order_id = ANY(v_oids); RAISE NOTICE '  inventory_movements    : %', n;
    SELECT count(*) INTO n FROM orders        WHERE customer_id = v_id;                          RAISE NOTICE '  orders                 : %', n;
    SELECT count(*) INTO n FROM invoices      WHERE customer_id = v_id;                          RAISE NOTICE '  invoices               : %', n;
    SELECT count(*) INTO n FROM customer_subscriptions WHERE customer_id = v_id;                 RAISE NOTICE '  customer_subscriptions : %', n;
    SELECT count(*) INTO n FROM service_requests WHERE customer_id = v_id;                       RAISE NOTICE '  service_requests       : %', n;
    SELECT count(*) INTO n FROM amc_subscriptions WHERE customer_id = v_id;                      RAISE NOTICE '  amc_subscriptions      : %', n;

    IF v_dry THEN
        RAISE NOTICE 'DRY RUN — nothing deleted. Set v_dry := false and re-run to delete.';
        RETURN;
    END IF;

    -- Delete children first, then parents (FK-safe order).
    DELETE FROM delivery_items
          WHERE delivery_id   IN (SELECT id FROM deliveries  WHERE order_id = ANY(v_oids))
             OR order_item_id IN (SELECT id FROM order_items WHERE order_id = ANY(v_oids));
    DELETE FROM payments            WHERE customer_id = v_id OR order_id = ANY(v_oids) OR invoice_id = ANY(v_iids);
    DELETE FROM deliveries          WHERE order_id   = ANY(v_oids);
    DELETE FROM order_items         WHERE order_id   = ANY(v_oids);
    DELETE FROM inventory_movements WHERE customer_id = v_id OR order_id = ANY(v_oids);
    DELETE FROM orders              WHERE customer_id = v_id;
    DELETE FROM invoices            WHERE customer_id = v_id;
    DELETE FROM customer_subscriptions WHERE customer_id = v_id;
    DELETE FROM service_requests    WHERE customer_id = v_id;
    DELETE FROM amc_subscriptions   WHERE customer_id = v_id;
    DELETE FROM customers           WHERE id = v_id;

    RAISE NOTICE 'Deleted customer % (%) and all related rows.', v_name, v_id;
END $$;
