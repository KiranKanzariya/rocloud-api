-- ============================================================
-- ROCloud — complete database schema
-- Guide §3 (tables, indexes, RLS, seed) + §4c.3 (localization)
--           + §10.7 (RLS tenant-isolation policies)
--
-- Apply as the postgres superuser AFTER create-db-and-role.sql:
--   psql -U postgres -d rocloud_dev -f schema.sql
--
-- Tables are created by postgres; ALTER DEFAULT PRIVILEGES from the
-- bootstrap script grants them to rocloud_user automatically.
-- ============================================================

-- ============================================================
-- EXTENSIONS
-- ============================================================
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- ============================================================
-- PLATFORM TABLES (no tenant_id — platform-wide)
-- ============================================================

CREATE TABLE plans (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name            VARCHAR(50)  NOT NULL,
    plan_type       VARCHAR(20)  NOT NULL CHECK (plan_type IN ('Basic','Pro','Enterprise')),
    monthly_price   DECIMAL(10,2) NOT NULL,
    yearly_price    DECIMAL(10,2) NOT NULL,
    max_customers   INT NOT NULL DEFAULT 200,
    max_users       INT NOT NULL DEFAULT 3,
    max_delivery_boys INT NOT NULL DEFAULT 1,
    whatsapp_enabled      BOOLEAN NOT NULL DEFAULT false,
    custom_roles_enabled  BOOLEAN NOT NULL DEFAULT false,
    multi_branch_enabled  BOOLEAN NOT NULL DEFAULT false,
    api_access_enabled    BOOLEAN NOT NULL DEFAULT false,
    is_active       BOOLEAN NOT NULL DEFAULT true,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ
);

CREATE TABLE tenants (
    id                      UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    plan_id                 UUID NOT NULL REFERENCES plans(id),
    name                    VARCHAR(200) NOT NULL,
    subdomain               VARCHAR(100) NOT NULL UNIQUE,
    owner_name              VARCHAR(200) NOT NULL,
    owner_email             VARCHAR(200) NOT NULL,
    owner_mobile            VARCHAR(15)  NOT NULL,
    logo_url                TEXT,
    primary_color           VARCHAR(7) DEFAULT '#0C447C',
    status                  VARCHAR(20) NOT NULL DEFAULT 'Trial'
                                CHECK (status IN ('Trial','Active','Suspended','Overdue','Cancelled')),
    trial_ends_at           TIMESTAMPTZ,
    subscription_ends_at    TIMESTAMPTZ,
    razorpay_subscription_id VARCHAR(100),
    razorpay_customer_id    VARCHAR(100),
    gst_number              VARCHAR(20),
    -- §24 — owner-configurable GST on customer invoices (rate stored as a fraction, e.g. 0.18 = 18%)
    gst_enabled             BOOLEAN NOT NULL DEFAULT true,
    gst_rate                NUMERIC(5,4) NOT NULL DEFAULT 0.18,
    address_line            TEXT,
    city                    VARCHAR(100),
    state                   VARCHAR(100),
    pincode                 VARCHAR(10),
    -- §4c.3 — tenant default language (set at registration)
    default_language        VARCHAR(5) NOT NULL DEFAULT 'en',
    is_deleted              BOOLEAN NOT NULL DEFAULT false,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ
);

CREATE UNIQUE INDEX idx_tenants_subdomain ON tenants(subdomain) WHERE is_deleted = false;
CREATE INDEX idx_tenants_status ON tenants(status) WHERE is_deleted = false;

CREATE TABLE platform_users (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name            VARCHAR(200) NOT NULL,
    email           VARCHAR(200) NOT NULL UNIQUE,
    password_hash   TEXT,
    platform_role   VARCHAR(30) NOT NULL DEFAULT 'Support'
                        CHECK (platform_role IN ('SuperAdmin','Support','Finance')),
    is_active       BOOLEAN NOT NULL DEFAULT true,
    last_login_at   TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ
);

-- ============================================================
-- RBAC TABLES (tenant-scoped)
-- ============================================================

CREATE TABLE permissions (
    id      UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    module  VARCHAR(50) NOT NULL,
    action  VARCHAR(50) NOT NULL,
    code    VARCHAR(100) NOT NULL UNIQUE  -- e.g. 'Customers.Create'
);

CREATE TABLE roles (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id   UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name        VARCHAR(100) NOT NULL,
    is_system   BOOLEAN NOT NULL DEFAULT false,
    is_custom   BOOLEAN NOT NULL DEFAULT false,
    is_deleted  BOOLEAN NOT NULL DEFAULT false,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_roles_tenant ON roles(tenant_id) WHERE is_deleted = false;

CREATE TABLE role_permissions (
    role_id       UUID NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    permission_id UUID NOT NULL REFERENCES permissions(id) ON DELETE CASCADE,
    PRIMARY KEY (role_id, permission_id)
);

-- ============================================================
-- USERS TABLE (tenant-scoped)
-- ============================================================

CREATE TABLE users (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    role_id         UUID REFERENCES roles(id),
    name            VARCHAR(200) NOT NULL,
    mobile          VARCHAR(15),
    email           VARCHAR(200),
    password_hash   TEXT,
    google_id       VARCHAR(200),            -- for Google OAuth
    google_email    VARCHAR(200),
    avatar_url      TEXT,
    auth_provider   VARCHAR(20) NOT NULL DEFAULT 'custom'
                        CHECK (auth_provider IN ('custom','google','both')),
    refresh_token   TEXT,
    device_token    TEXT,                    -- for mobile push
    -- §4c.3 — per-user language override
    preferred_language VARCHAR(5),
    is_active       BOOLEAN NOT NULL DEFAULT true,
    last_login_at   TIMESTAMPTZ,
    is_deleted      BOOLEAN NOT NULL DEFAULT false,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ
);

CREATE INDEX idx_users_tenant ON users(tenant_id) WHERE is_deleted = false;
CREATE UNIQUE INDEX idx_users_email_tenant ON users(tenant_id, email) WHERE is_deleted = false AND email IS NOT NULL;
CREATE UNIQUE INDEX idx_users_google ON users(google_id) WHERE google_id IS NOT NULL;

-- ============================================================
-- AREAS TABLE
-- ============================================================

CREATE TABLE areas (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id   UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name        VARCHAR(100) NOT NULL,
    city        VARCHAR(100),
    pincode     VARCHAR(10),
    is_active   BOOLEAN NOT NULL DEFAULT true,
    is_deleted  BOOLEAN NOT NULL DEFAULT false,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_areas_tenant ON areas(tenant_id) WHERE is_deleted = false;

-- ============================================================
-- USER ↔ AREA ASSIGNMENTS (many-to-many).
-- A team member (delivery boy) can serve several areas.
-- ============================================================

CREATE TABLE user_areas (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id   UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    area_id     UUID NOT NULL REFERENCES areas(id) ON DELETE CASCADE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (user_id, area_id)
);

CREATE INDEX idx_user_areas_user ON user_areas(tenant_id, user_id);
CREATE INDEX idx_user_areas_area ON user_areas(tenant_id, area_id);

-- ============================================================
-- PRODUCTS TABLE
-- ============================================================

CREATE TABLE products (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name            VARCHAR(200) NOT NULL,
    bottle_size     VARCHAR(20) NOT NULL
                        CHECK (bottle_size IN ('18L','20L','250ml','500ml','1L','Custom')),
    default_rate    DECIMAL(10,2) NOT NULL,
    unit            VARCHAR(20) NOT NULL DEFAULT 'bottle',
    is_active       BOOLEAN NOT NULL DEFAULT true,
    is_deleted      BOOLEAN NOT NULL DEFAULT false,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ
);

CREATE INDEX idx_products_tenant ON products(tenant_id) WHERE is_deleted = false;

-- ============================================================
-- CUSTOMERS TABLE
-- ============================================================

CREATE TABLE customers (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    area_id             UUID REFERENCES areas(id),
    customer_code       VARCHAR(20),
    name                VARCHAR(200) NOT NULL,
    mobile              VARCHAR(15)  NOT NULL,
    alternate_mobile    VARCHAR(15),
    email               VARCHAR(200),
    address_line        TEXT,
    landmark            VARCHAR(200),
    latitude            DECIMAL(10,8),
    longitude           DECIMAL(11,8),
    delivery_mode       VARCHAR(20) NOT NULL DEFAULT 'HomeDelivery'
                            CHECK (delivery_mode IN ('HomeDelivery','PlantPickup','Both')),
    payment_preference  VARCHAR(20) NOT NULL DEFAULT 'PerBottle'
                            CHECK (payment_preference IN ('PerBottle','Weekly','Monthly','Combined')),
    preferred_bottle_size VARCHAR(20) DEFAULT '20L',
    -- §4c.3 — per-customer language (invoices, WhatsApp messages)
    preferred_language  VARCHAR(5),
    notes               TEXT,
    is_active           BOOLEAN NOT NULL DEFAULT true,
    is_deleted          BOOLEAN NOT NULL DEFAULT false,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ
);

CREATE INDEX idx_customers_tenant ON customers(tenant_id) WHERE is_deleted = false;
CREATE INDEX idx_customers_area ON customers(tenant_id, area_id) WHERE is_deleted = false;
CREATE INDEX idx_customers_mobile ON customers(tenant_id, mobile) WHERE is_deleted = false;

-- ============================================================
-- CUSTOMER SUBSCRIPTIONS
-- ============================================================

CREATE TABLE customer_subscriptions (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    customer_id     UUID NOT NULL REFERENCES customers(id),
    product_id      UUID NOT NULL REFERENCES products(id),
    quantity        INT NOT NULL DEFAULT 1,
    frequency       VARCHAR(20) NOT NULL DEFAULT 'Daily'
                        CHECK (frequency IN ('Daily','AlternateDay','Weekly','Monthly','Custom')),
    rate_per_unit   DECIMAL(10,2) NOT NULL,
    start_date      DATE NOT NULL,
    end_date        DATE,
    is_active       BOOLEAN NOT NULL DEFAULT true,
    is_deleted      BOOLEAN NOT NULL DEFAULT false,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ
);

CREATE INDEX idx_cust_subs_tenant ON customer_subscriptions(tenant_id) WHERE is_deleted = false;
CREATE INDEX idx_cust_subs_customer ON customer_subscriptions(customer_id) WHERE is_deleted = false;

-- ============================================================
-- ORDERS TABLE
-- ============================================================

CREATE TABLE orders (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    customer_id         UUID NOT NULL REFERENCES customers(id),
    delivery_boy_id     UUID REFERENCES users(id),
    area_id             UUID REFERENCES areas(id),
    order_date          DATE NOT NULL DEFAULT CURRENT_DATE,
    order_type          VARCHAR(20) NOT NULL DEFAULT 'Regular'
                            CHECK (order_type IN ('Regular','Urgent','Subscription','BulkReturn','Advance')),
    delivery_mode       VARCHAR(20) NOT NULL DEFAULT 'HomeDelivery'
                            CHECK (delivery_mode IN ('HomeDelivery','PlantPickup')),
    status              VARCHAR(20) NOT NULL DEFAULT 'Pending'
                            CHECK (status IN ('Pending','Confirmed','InTransit','Delivered','Cancelled','Returned')),
    notes               TEXT,
    is_deleted          BOOLEAN NOT NULL DEFAULT false,
    created_by          UUID REFERENCES users(id),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ
);

CREATE INDEX idx_orders_tenant_date ON orders(tenant_id, order_date DESC) WHERE is_deleted = false;
CREATE INDEX idx_orders_delivery_boy ON orders(tenant_id, delivery_boy_id, order_date) WHERE is_deleted = false;
CREATE INDEX idx_orders_customer ON orders(customer_id, order_date DESC) WHERE is_deleted = false;

-- ============================================================
-- ORDER ITEMS
-- ============================================================

CREATE TABLE order_items (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id   UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    order_id    UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    product_id  UUID NOT NULL REFERENCES products(id),
    quantity    INT NOT NULL DEFAULT 1,
    unit_rate   DECIMAL(10,2) NOT NULL,
    total_amount DECIMAL(10,2) GENERATED ALWAYS AS (quantity * unit_rate) STORED
);

CREATE INDEX idx_order_items_order ON order_items(order_id);

-- ============================================================
-- DELIVERIES TABLE
-- ============================================================

CREATE TABLE deliveries (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    order_id            UUID NOT NULL REFERENCES orders(id),
    delivery_boy_id     UUID REFERENCES users(id),
    scheduled_date      DATE NOT NULL,
    delivered_at        TIMESTAMPTZ,
    status              VARCHAR(20) NOT NULL DEFAULT 'Pending'
                            CHECK (status IN ('Pending','InTransit','Delivered','Failed','Skipped')),
    jars_delivered      INT DEFAULT 0,
    jars_returned       INT DEFAULT 0,
    collected_amount    DECIMAL(10,2) DEFAULT 0,
    payment_method      VARCHAR(20) CHECK (payment_method IN ('Cash','UPI','Card','Online','None')),
    proof_image_url     TEXT,
    latitude            DECIMAL(10,8),
    longitude           DECIMAL(11,8),
    notes               TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ
);

CREATE INDEX idx_deliveries_tenant_date ON deliveries(tenant_id, scheduled_date DESC);
CREATE INDEX idx_deliveries_boy_date ON deliveries(tenant_id, delivery_boy_id, scheduled_date);

-- Per-product jars delivered/returned for a delivery (multi-item orders, §9).
CREATE TABLE delivery_items (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    delivery_id         UUID NOT NULL REFERENCES deliveries(id) ON DELETE CASCADE,
    order_item_id       UUID NOT NULL REFERENCES order_items(id),
    product_id          UUID NOT NULL REFERENCES products(id),
    jars_delivered      INT NOT NULL DEFAULT 0,
    jars_returned       INT NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_delivery_items_delivery ON delivery_items(delivery_id);

-- ============================================================
-- INVENTORY TABLE
-- ============================================================

CREATE TABLE inventory (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    product_id      UUID NOT NULL REFERENCES products(id),
    total_stock     INT NOT NULL DEFAULT 0,
    issued_stock    INT NOT NULL DEFAULT 0,
    returned_stock  INT NOT NULL DEFAULT 0,
    damaged_stock   INT NOT NULL DEFAULT 0,
    last_updated    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (tenant_id, product_id)
);

CREATE INDEX idx_inventory_tenant ON inventory(tenant_id);

CREATE TABLE inventory_movements (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    product_id      UUID NOT NULL REFERENCES products(id),
    order_id        UUID REFERENCES orders(id),
    customer_id     UUID REFERENCES customers(id),
    movement_type   VARCHAR(20) NOT NULL
                        CHECK (movement_type IN ('Issue','Return','Damage','Restock','Adjustment')),
    quantity        INT NOT NULL,
    performed_by    UUID REFERENCES users(id),
    notes           TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_inv_movements_tenant ON inventory_movements(tenant_id, created_at DESC);

-- ============================================================
-- INVOICES TABLE
-- ============================================================

CREATE TABLE invoices (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    customer_id     UUID NOT NULL REFERENCES customers(id),
    invoice_number  VARCHAR(50) NOT NULL,
    invoice_date    DATE NOT NULL DEFAULT CURRENT_DATE,
    due_date        DATE NOT NULL,
    period_from     DATE,
    period_to       DATE,
    sub_total       DECIMAL(10,2) NOT NULL DEFAULT 0,
    tax_amount      DECIMAL(10,2) NOT NULL DEFAULT 0,
    discount        DECIMAL(10,2) NOT NULL DEFAULT 0,
    total_amount    DECIMAL(10,2) NOT NULL DEFAULT 0,
    paid_amount     DECIMAL(10,2) NOT NULL DEFAULT 0,
    status          VARCHAR(20) NOT NULL DEFAULT 'Draft'
                        CHECK (status IN ('Draft','Sent','Paid','PartiallyPaid','Overdue','Cancelled')),
    gst_number      VARCHAR(20),
    notes           TEXT,
    pdf_url         TEXT,
    is_deleted      BOOLEAN NOT NULL DEFAULT false,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ
);

CREATE UNIQUE INDEX idx_invoices_number ON invoices(tenant_id, invoice_number) WHERE is_deleted = false;
CREATE INDEX idx_invoices_customer ON invoices(tenant_id, customer_id, status) WHERE is_deleted = false;
CREATE INDEX idx_invoices_status ON invoices(tenant_id, status, due_date) WHERE is_deleted = false;

-- ============================================================
-- PAYMENTS TABLE
-- ============================================================

CREATE TABLE payments (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    customer_id         UUID NOT NULL REFERENCES customers(id),
    invoice_id          UUID REFERENCES invoices(id),
    order_id            UUID REFERENCES orders(id),
    amount              DECIMAL(10,2) NOT NULL,
    payment_method      VARCHAR(20) NOT NULL
                            CHECK (payment_method IN ('Cash','UPI','Card','Online','BankTransfer')),
    payment_preference  VARCHAR(20)
                            CHECK (payment_preference IN ('PerBottle','Weekly','Monthly','Combined')),
    status              VARCHAR(20) NOT NULL DEFAULT 'Completed'
                            CHECK (status IN ('Pending','Completed','Failed','Refunded')),
    reference_number    VARCHAR(100),
    razorpay_payment_id VARCHAR(100),
    collected_by        UUID REFERENCES users(id),
    paid_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    notes               TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_payments_tenant ON payments(tenant_id, paid_at DESC);
CREATE INDEX idx_payments_customer ON payments(tenant_id, customer_id, paid_at DESC);

-- ============================================================
-- SERVICE REQUESTS TABLE
-- ============================================================

CREATE TABLE service_requests (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    customer_id         UUID NOT NULL REFERENCES customers(id),
    assigned_tech_id    UUID REFERENCES users(id),
    ticket_number       VARCHAR(20) NOT NULL,
    title               VARCHAR(200) NOT NULL,
    description         TEXT,
    service_type        VARCHAR(30) NOT NULL
                            CHECK (service_type IN ('FilterChange','MembraneReplace','Complaint','RoutineAMC','Installation','Other')),
    status              VARCHAR(20) NOT NULL DEFAULT 'Open'
                            CHECK (status IN ('Open','InProgress','Resolved','Cancelled')),
    priority            VARCHAR(10) NOT NULL DEFAULT 'Medium'
                            CHECK (priority IN ('Low','Medium','High','Urgent')),
    scheduled_date      DATE,
    resolved_at         TIMESTAMPTZ,
    resolution_notes    TEXT,
    is_deleted          BOOLEAN NOT NULL DEFAULT false,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ
);

CREATE INDEX idx_sr_tenant ON service_requests(tenant_id, status) WHERE is_deleted = false;
CREATE INDEX idx_sr_tech ON service_requests(tenant_id, assigned_tech_id, status) WHERE is_deleted = false;

-- ============================================================
-- AMC SUBSCRIPTIONS — per-customer Annual Maintenance Contracts.
-- Drives routine AMC visit scheduling: next_due_date is advanced
-- by interval_months each time a visit is scheduled.
-- ============================================================

CREATE TABLE amc_subscriptions (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    customer_id         UUID NOT NULL REFERENCES customers(id),
    plan_name           VARCHAR(100),
    interval_months     INT NOT NULL CHECK (interval_months IN (3, 6, 12)),
    amount              DECIMAL(10,2) NOT NULL DEFAULT 0,
    start_date          DATE NOT NULL,
    end_date            DATE,
    last_service_date   DATE,
    next_due_date       DATE NOT NULL,
    is_active           BOOLEAN NOT NULL DEFAULT true,
    is_deleted          BOOLEAN NOT NULL DEFAULT false,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ
);

CREATE INDEX idx_amc_customer ON amc_subscriptions(tenant_id, customer_id) WHERE is_deleted = false;
CREATE INDEX idx_amc_due ON amc_subscriptions(tenant_id, next_due_date) WHERE is_active = true AND is_deleted = false;

-- ============================================================
-- NOTIFICATION TEMPLATES (§4c.3) — DB-backed i18n templates
-- One row per (template_code, language, channel).
-- tenant_id NULL = system default, else tenant override.
-- ============================================================

CREATE TABLE notification_templates (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       UUID REFERENCES tenants(id) ON DELETE CASCADE,
    template_code   VARCHAR(50) NOT NULL,
                    -- e.g. 'invoice_sent', 'payment_reminder', 'delivery_confirmation'
    language_code   VARCHAR(5)  NOT NULL,
    channel         VARCHAR(20) NOT NULL
                        CHECK (channel IN ('Email','SMS','WhatsApp')),
    subject         VARCHAR(200),
    body            TEXT NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ,
    UNIQUE (tenant_id, template_code, language_code, channel)
);

CREATE INDEX idx_notif_templates_lookup
    ON notification_templates(COALESCE(tenant_id, '00000000-0000-0000-0000-000000000000'::uuid),
                              template_code, language_code, channel);

-- ============================================================
-- NOTIFICATIONS (§24) — the owner's in-app bell feed.
-- One row per (tenant, user, type); generated from actionable
-- tenant state (overdue invoices, pending orders, AMC due,
-- open service requests) and updated in place as counts change.
-- ============================================================

CREATE TABLE notifications (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    user_id         UUID NOT NULL REFERENCES users(id)   ON DELETE CASCADE,
    type            VARCHAR(40)  NOT NULL,
                        -- InvoicesOverdue | OrdersPending | AmcDue | ServiceOpen
    title           VARCHAR(200) NOT NULL,
    link            VARCHAR(200),
    reference_key   VARCHAR(120) NOT NULL, -- change-detection token (the current count)
    is_read         BOOLEAN NOT NULL DEFAULT false,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ,
    UNIQUE (tenant_id, user_id, type)
);

CREATE INDEX idx_notifications_unread ON notifications(tenant_id, user_id, is_read);

-- ============================================================
-- AUDIT LOGS TABLE (partitioned by month)
-- ============================================================

CREATE TABLE audit_logs (
    id          UUID NOT NULL DEFAULT uuid_generate_v4(),
    tenant_id   UUID REFERENCES tenants(id),
    user_id     UUID REFERENCES users(id),
    module      VARCHAR(50) NOT NULL,
    action      VARCHAR(50) NOT NULL,
    entity_name VARCHAR(100),
    entity_id   UUID,
    old_values  JSONB,
    new_values  JSONB,
    ip_address  VARCHAR(45),
    user_agent  TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (id, created_at)
) PARTITION BY RANGE (created_at);

-- First 3 monthly partitions: current month (Jun 2026) + next 2 (Jul, Aug 2026).
-- AuditLogPartitionJob (Phase 14) creates subsequent partitions monthly.
CREATE TABLE audit_logs_2026_06 PARTITION OF audit_logs
    FOR VALUES FROM ('2026-06-01') TO ('2026-07-01');
CREATE TABLE audit_logs_2026_07 PARTITION OF audit_logs
    FOR VALUES FROM ('2026-07-01') TO ('2026-08-01');
CREATE TABLE audit_logs_2026_08 PARTITION OF audit_logs
    FOR VALUES FROM ('2026-08-01') TO ('2026-09-01');

CREATE INDEX idx_audit_tenant_date ON audit_logs(tenant_id, created_at DESC);

-- ============================================================
-- ROW-LEVEL SECURITY (defence-in-depth) — §3 + §10.7
-- ============================================================

ALTER TABLE customers  ENABLE ROW LEVEL SECURITY;
ALTER TABLE orders     ENABLE ROW LEVEL SECURITY;
ALTER TABLE deliveries ENABLE ROW LEVEL SECURITY;
ALTER TABLE invoices   ENABLE ROW LEVEL SECURITY;
ALTER TABLE payments   ENABLE ROW LEVEL SECURITY;

-- Tenant-isolation policies (§10.7). TenantMiddleware sets the session var
-- `app.current_tenant_id` per request (Phase 6). The bypass role below skips these.
CREATE POLICY tenant_isolation ON customers
    USING (tenant_id = current_setting('app.current_tenant_id', true)::uuid);
CREATE POLICY tenant_isolation ON orders
    USING (tenant_id = current_setting('app.current_tenant_id', true)::uuid);
CREATE POLICY tenant_isolation ON deliveries
    USING (tenant_id = current_setting('app.current_tenant_id', true)::uuid);
CREATE POLICY tenant_isolation ON invoices
    USING (tenant_id = current_setting('app.current_tenant_id', true)::uuid);
CREATE POLICY tenant_isolation ON payments
    USING (tenant_id = current_setting('app.current_tenant_id', true)::uuid);

-- App role bypasses RLS (connection pool user). §3.
-- NOTE: change this password before any real deployment.
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'rocloud_app') THEN
        CREATE ROLE rocloud_app LOGIN PASSWORD 'CHANGE_ME_app_password';
    END IF;
END
$$;
GRANT ALL ON ALL TABLES IN SCHEMA public TO rocloud_app;
ALTER ROLE rocloud_app BYPASSRLS;

-- ============================================================
-- SEED DATA — PLANS (3)
-- ============================================================

INSERT INTO plans (name, plan_type, monthly_price, yearly_price, max_customers, max_users, max_delivery_boys, whatsapp_enabled, custom_roles_enabled, multi_branch_enabled) VALUES
('Basic',      'Basic',      999.00,  9990.00,  200,  3,  1, false, false, false),
('Pro',        'Pro',       2499.00, 24990.00, 1000, 10,  5, true,  false, false),
('Enterprise', 'Enterprise', 5999.00, 59990.00, 99999, 99, 99, true, true,  true);

-- ============================================================
-- SEED DATA — PERMISSIONS (28)
-- ============================================================

INSERT INTO permissions (module, action, code) VALUES
('Customers',  'View',    'Customers.View'),
('Customers',  'Create',  'Customers.Create'),
('Customers',  'Edit',    'Customers.Edit'),
('Customers',  'Delete',  'Customers.Delete'),
('Orders',     'View',    'Orders.View'),
('Orders',     'Create',  'Orders.Create'),
('Orders',     'Edit',    'Orders.Edit'),
('Orders',     'Cancel',  'Orders.Cancel'),
('Deliveries', 'View',    'Deliveries.View'),
('Deliveries', 'Update',  'Deliveries.Update'),
('Deliveries', 'ViewOwn', 'Deliveries.ViewOwn'),
('Inventory',  'View',    'Inventory.View'),
('Inventory',  'Manage',  'Inventory.Manage'),
('Invoices',   'View',    'Invoices.View'),
('Invoices',   'Create',  'Invoices.Create'),
('Invoices',   'Edit',    'Invoices.Edit'),
('Payments',   'View',    'Payments.View'),
('Payments',   'Collect', 'Payments.Collect'),
('Payments',   'Manage',  'Payments.Manage'),
('Reports',    'View',    'Reports.View'),
('AMC',        'View',    'AMC.View'),
('AMC',        'Manage',  'AMC.Manage'),
('AMC',        'Update',  'AMC.Update'),
('Users',      'View',    'Users.View'),
('Users',      'Manage',  'Users.Manage'),
('Roles',      'Manage',  'Roles.Manage'),
('Settings',   'View',    'Settings.View'),
('Settings',   'Manage',  'Settings.Manage');
