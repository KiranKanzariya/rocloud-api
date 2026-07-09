-- Widens the orders.order_type CHECK constraint to allow the new 'Advance' value (future-dated
-- one-time event/program bookings). The enum is persisted as a string; without this the app's
-- INSERT/UPDATE of an Advance order would fail the CHECK constraint.
--
-- Run AS THE DATABASE OWNER (postgres) — orders is a postgres-owned, RLS-protected table.
-- Apply to BOTH dev and prod.

ALTER TABLE orders DROP CONSTRAINT IF EXISTS orders_order_type_check;

ALTER TABLE orders ADD CONSTRAINT orders_order_type_check
    CHECK ((order_type)::text = ANY ((ARRAY[
        'Regular'::character varying,
        'Urgent'::character varying,
        'Subscription'::character varying,
        'BulkReturn'::character varying,
        'Advance'::character varying
    ])::text[]));
