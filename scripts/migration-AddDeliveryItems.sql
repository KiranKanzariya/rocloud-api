-- Per-product jars delivered/returned on a delivery (guide §9), so a multi-item order can record how
-- many of each product were handed over and how many empties came back.
--
-- References tenants(id), deliveries(id), order_items(id), products(id) — all owned by the postgres
-- superuser, so run this AS THE DATABASE OWNER (postgres). grant.sql's ALTER DEFAULT PRIVILEGES then
-- grants the app role (rocloud_dev_user / rocloud_app) automatically:
--   "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -d rocloud_dev -f scripts\migration-AddDeliveryItems.sql

CREATE TABLE IF NOT EXISTS delivery_items (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    delivery_id         UUID NOT NULL REFERENCES deliveries(id) ON DELETE CASCADE,
    order_item_id       UUID NOT NULL REFERENCES order_items(id),
    product_id          UUID NOT NULL REFERENCES products(id),
    jars_delivered      INT NOT NULL DEFAULT 0,
    jars_returned       INT NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_delivery_items_delivery ON delivery_items(delivery_id);

-- If your install does NOT use grant.sql's ALTER DEFAULT PRIVILEGES, grant explicitly:
--   GRANT ALL PRIVILEGES ON delivery_items TO rocloud_dev_user;  -- dev
--   GRANT ALL PRIVILEGES ON delivery_items TO rocloud_app;       -- prod
