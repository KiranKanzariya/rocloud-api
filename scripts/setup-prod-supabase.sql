-- ════════════════════════════════════════════════════════════════════════════
-- ROCloud production database — SUPABASE setup (single script).
--
-- Supabase note: a project IS one database ("postgres") — you do NOT run
-- CREATE DATABASE. Run this whole script in the Supabase SQL Editor (or via psql
-- with the project connection string) as the project's "postgres" role.
--
-- It contains the CURRENT live schema (generated from the running database, so it
-- includes every migration — no drift), adapted for Supabase:
--   • uuid-ossp replaced by core gen_random_uuid()  (no extension needed)
--   • psql \restrict meta-commands stripped (the SQL Editor isn't psql)
--   • objects are owned by "postgres"; a dedicated, non-owner login role
--     (rocloud_app) is created so Row-Level Security applies to the app.
--
-- ⚠ The app MUST connect as rocloud_app (NOT postgres). If it connects as the
--   table owner, RLS is bypassed and tenants would see each other's data.
--
-- AFTER this script:
--   1. Run scripts/create-platform-admin.sql in a new SQL Editor tab (edit the
--      email/password first) to create your super-admin login.
--   2. In C:\inetpub\rocloud-api\web.config set ConnectionStrings__Default to the
--      Supabase connection, e.g.
--        Host=db.<project-ref>.supabase.co;Port=5432;Database=postgres;
--        Username=rocloud_app;Password=<the password set below>;
--        SSL Mode=Require;Trust Server Certificate=true
--      (use the Session/Direct connection — port 5432 — for a long-lived API.)
--      Recycle the IIS app pool.
-- ════════════════════════════════════════════════════════════════════════════

-- pgcrypto is used by create-platform-admin.sql (BCrypt hash). Pre-installed on
-- Supabase; this is a harmless no-op if already present.
CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA extensions;


-- ─────────────────────────────────────────────────────────────────────────────
-- CURRENT SCHEMA (tables, indexes, RLS policies, audit_logs partitions)
-- ─────────────────────────────────────────────────────────────────────────────
--
-- PostgreSQL database dump
--


-- Dumped from database version 18.4
-- Dumped by pg_dump version 18.4

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: pgcrypto; Type: EXTENSION; Schema: -; Owner: -
--



--
-- Name: EXTENSION pgcrypto; Type: COMMENT; Schema: -; Owner: -
--



--
-- Name: uuid-ossp; Type: EXTENSION; Schema: -; Owner: -
--



--
-- Name: EXTENSION "uuid-ossp"; Type: COMMENT; Schema: -; Owner: -
--



SET default_table_access_method = heap;

--
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


--
-- Name: amc_subscriptions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.amc_subscriptions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    customer_id uuid NOT NULL,
    plan_name character varying(100),
    interval_months integer NOT NULL,
    amount numeric(10,2) NOT NULL,
    start_date date NOT NULL,
    end_date date,
    last_service_date date,
    next_due_date date NOT NULL,
    is_active boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    is_deleted boolean NOT NULL,
    CONSTRAINT ck_amc_subscriptions_interval CHECK ((interval_months = ANY (ARRAY[3, 6, 12])))
);


--
-- Name: areas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.areas (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    city character varying(100),
    pincode character varying(10),
    is_active boolean DEFAULT true NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: audit_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.audit_logs (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid,
    user_id uuid,
    module character varying(50) NOT NULL,
    action character varying(50) NOT NULL,
    entity_name character varying(100),
    entity_id uuid,
    old_values jsonb,
    new_values jsonb,
    ip_address character varying(45),
    user_agent text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    status_code integer
)
PARTITION BY RANGE (created_at);


--
-- Name: audit_logs_2026_06; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.audit_logs_2026_06 (
    id uuid DEFAULT gen_random_uuid() CONSTRAINT audit_logs_id_not_null NOT NULL,
    tenant_id uuid,
    user_id uuid,
    module character varying(50) CONSTRAINT audit_logs_module_not_null NOT NULL,
    action character varying(50) CONSTRAINT audit_logs_action_not_null NOT NULL,
    entity_name character varying(100),
    entity_id uuid,
    old_values jsonb,
    new_values jsonb,
    ip_address character varying(45),
    user_agent text,
    created_at timestamp with time zone DEFAULT now() CONSTRAINT audit_logs_created_at_not_null NOT NULL,
    status_code integer
);


--
-- Name: audit_logs_2026_07; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.audit_logs_2026_07 (
    id uuid DEFAULT gen_random_uuid() CONSTRAINT audit_logs_id_not_null NOT NULL,
    tenant_id uuid,
    user_id uuid,
    module character varying(50) CONSTRAINT audit_logs_module_not_null NOT NULL,
    action character varying(50) CONSTRAINT audit_logs_action_not_null NOT NULL,
    entity_name character varying(100),
    entity_id uuid,
    old_values jsonb,
    new_values jsonb,
    ip_address character varying(45),
    user_agent text,
    created_at timestamp with time zone DEFAULT now() CONSTRAINT audit_logs_created_at_not_null NOT NULL,
    status_code integer
);


--
-- Name: audit_logs_2026_08; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.audit_logs_2026_08 (
    id uuid DEFAULT gen_random_uuid() CONSTRAINT audit_logs_id_not_null NOT NULL,
    tenant_id uuid,
    user_id uuid,
    module character varying(50) CONSTRAINT audit_logs_module_not_null NOT NULL,
    action character varying(50) CONSTRAINT audit_logs_action_not_null NOT NULL,
    entity_name character varying(100),
    entity_id uuid,
    old_values jsonb,
    new_values jsonb,
    ip_address character varying(45),
    user_agent text,
    created_at timestamp with time zone DEFAULT now() CONSTRAINT audit_logs_created_at_not_null NOT NULL,
    status_code integer
);


--
-- Name: audit_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.audit_settings (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    enabled boolean DEFAULT true NOT NULL,
    capture_request_body boolean DEFAULT true NOT NULL,
    max_request_body_bytes integer DEFAULT 102400 NOT NULL,
    methods text[] DEFAULT '{POST,PUT,PATCH,DELETE}'::text[] NOT NULL,
    sensitive_path_prefixes text[] DEFAULT '{/api/auth,/api/payments}'::text[] NOT NULL,
    exclude_modules text[] DEFAULT '{}'::text[] NOT NULL,
    audit_reads_for_modules text[] DEFAULT '{}'::text[] NOT NULL,
    additional_redact_keys text[] DEFAULT '{}'::text[] NOT NULL,
    retention_months integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    updated_by uuid
);


--
-- Name: customer_subscriptions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_subscriptions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    customer_id uuid NOT NULL,
    product_id uuid NOT NULL,
    quantity integer DEFAULT 1 NOT NULL,
    frequency character varying(20) DEFAULT 'Daily'::character varying NOT NULL,
    rate_per_unit numeric(10,2) NOT NULL,
    start_date date NOT NULL,
    end_date date,
    is_active boolean DEFAULT true NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT customer_subscriptions_frequency_check CHECK (((frequency)::text = ANY ((ARRAY['Daily'::character varying, 'AlternateDay'::character varying, 'Weekly'::character varying, 'Monthly'::character varying, 'Custom'::character varying])::text[])))
);


--
-- Name: customers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customers (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    area_id uuid,
    customer_code character varying(20),
    name character varying(200) NOT NULL,
    mobile character varying(15),
    alternate_mobile character varying(15),
    email character varying(200),
    address_line text,
    landmark character varying(200),
    latitude numeric(10,8),
    longitude numeric(11,8),
    delivery_mode character varying(20) DEFAULT 'HomeDelivery'::character varying NOT NULL,
    payment_preference character varying(20) DEFAULT 'PerBottle'::character varying NOT NULL,
    preferred_bottle_size character varying(20) DEFAULT '20L'::character varying,
    preferred_language character varying(5),
    notes text,
    is_active boolean DEFAULT true NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    discount_type character varying(20) DEFAULT 'None'::character varying NOT NULL,
    discount_value numeric(10,2) DEFAULT 0 NOT NULL,
    CONSTRAINT customers_delivery_mode_check CHECK (((delivery_mode)::text = ANY ((ARRAY['HomeDelivery'::character varying, 'PlantPickup'::character varying, 'Both'::character varying])::text[]))),
    CONSTRAINT customers_discount_type_check CHECK (((discount_type)::text = ANY ((ARRAY['None'::character varying, 'Percentage'::character varying, 'Fixed'::character varying])::text[]))),
    CONSTRAINT customers_payment_preference_check CHECK (((payment_preference)::text = ANY ((ARRAY['PerBottle'::character varying, 'Weekly'::character varying, 'Monthly'::character varying, 'Combined'::character varying])::text[])))
);


--
-- Name: deliveries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.deliveries (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    order_id uuid NOT NULL,
    delivery_boy_id uuid,
    scheduled_date date NOT NULL,
    delivered_at timestamp with time zone,
    status character varying(20) DEFAULT 'Pending'::character varying NOT NULL,
    jars_delivered integer DEFAULT 0,
    jars_returned integer DEFAULT 0,
    collected_amount numeric(10,2) DEFAULT 0,
    payment_method character varying(20),
    proof_image_url text,
    latitude numeric(10,8),
    longitude numeric(11,8),
    notes text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT deliveries_payment_method_check CHECK (((payment_method)::text = ANY ((ARRAY['Cash'::character varying, 'UPI'::character varying, 'Card'::character varying, 'Online'::character varying, 'None'::character varying])::text[]))),
    CONSTRAINT deliveries_status_check CHECK (((status)::text = ANY ((ARRAY['Pending'::character varying, 'InTransit'::character varying, 'Delivered'::character varying, 'Failed'::character varying, 'Skipped'::character varying])::text[])))
);


--
-- Name: delivery_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.delivery_items (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    delivery_id uuid NOT NULL,
    order_item_id uuid NOT NULL,
    product_id uuid NOT NULL,
    jars_delivered integer DEFAULT 0 NOT NULL,
    jars_returned integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: inventory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inventory (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    product_id uuid NOT NULL,
    total_stock integer DEFAULT 0 NOT NULL,
    issued_stock integer DEFAULT 0 NOT NULL,
    returned_stock integer DEFAULT 0 NOT NULL,
    damaged_stock integer DEFAULT 0 NOT NULL,
    last_updated timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: inventory_movements; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inventory_movements (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    product_id uuid NOT NULL,
    order_id uuid,
    customer_id uuid,
    movement_type character varying(20) NOT NULL,
    quantity integer NOT NULL,
    performed_by uuid,
    notes text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT inventory_movements_movement_type_check CHECK (((movement_type)::text = ANY ((ARRAY['Issue'::character varying, 'Return'::character varying, 'Damage'::character varying, 'Restock'::character varying, 'Adjustment'::character varying])::text[])))
);


--
-- Name: invoices; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.invoices (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    customer_id uuid NOT NULL,
    invoice_number character varying(50) NOT NULL,
    invoice_date date DEFAULT CURRENT_DATE NOT NULL,
    due_date date NOT NULL,
    period_from date,
    period_to date,
    sub_total numeric(10,2) DEFAULT 0 NOT NULL,
    tax_amount numeric(10,2) DEFAULT 0 NOT NULL,
    discount numeric(10,2) DEFAULT 0 NOT NULL,
    total_amount numeric(10,2) DEFAULT 0 NOT NULL,
    paid_amount numeric(10,2) DEFAULT 0 NOT NULL,
    status character varying(20) DEFAULT 'Draft'::character varying NOT NULL,
    gst_number character varying(20),
    notes text,
    pdf_url text,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT invoices_status_check CHECK (((status)::text = ANY ((ARRAY['Draft'::character varying, 'Sent'::character varying, 'Paid'::character varying, 'PartiallyPaid'::character varying, 'Overdue'::character varying, 'Cancelled'::character varying])::text[])))
);


--
-- Name: logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.logs (
    message text,
    message_template text,
    level integer,
    "timestamp" timestamp without time zone,
    exception text,
    log_event jsonb,
    properties jsonb
);


--
-- Name: notification_templates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.notification_templates (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid,
    template_code character varying(50) NOT NULL,
    language_code character varying(5) NOT NULL,
    channel character varying(20) NOT NULL,
    subject character varying(200),
    body text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT notification_templates_channel_check CHECK (((channel)::text = ANY ((ARRAY['Email'::character varying, 'SMS'::character varying, 'WhatsApp'::character varying])::text[])))
);


--
-- Name: notifications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.notifications (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    type character varying(40) NOT NULL,
    title character varying(200) NOT NULL,
    link character varying(200),
    reference_key character varying(120) NOT NULL,
    is_read boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: order_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.order_items (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    order_id uuid NOT NULL,
    product_id uuid NOT NULL,
    quantity integer DEFAULT 1 NOT NULL,
    unit_rate numeric(10,2) NOT NULL,
    total_amount numeric(10,2) GENERATED ALWAYS AS (((quantity)::numeric * unit_rate)) STORED
);


--
-- Name: orders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.orders (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    customer_id uuid NOT NULL,
    delivery_boy_id uuid,
    area_id uuid,
    order_date date DEFAULT CURRENT_DATE NOT NULL,
    order_type character varying(20) DEFAULT 'Regular'::character varying NOT NULL,
    delivery_mode character varying(20) DEFAULT 'HomeDelivery'::character varying NOT NULL,
    status character varying(20) DEFAULT 'Pending'::character varying NOT NULL,
    notes text,
    is_deleted boolean DEFAULT false NOT NULL,
    created_by uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT orders_delivery_mode_check CHECK (((delivery_mode)::text = ANY ((ARRAY['HomeDelivery'::character varying, 'PlantPickup'::character varying])::text[]))),
    CONSTRAINT orders_order_type_check CHECK (((order_type)::text = ANY ((ARRAY['Regular'::character varying, 'Urgent'::character varying, 'Subscription'::character varying, 'BulkReturn'::character varying, 'Advance'::character varying])::text[]))),
    CONSTRAINT orders_status_check CHECK (((status)::text = ANY ((ARRAY['Pending'::character varying, 'Confirmed'::character varying, 'InTransit'::character varying, 'Delivered'::character varying, 'Cancelled'::character varying, 'Returned'::character varying])::text[])))
);


--
-- Name: payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payments (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    customer_id uuid NOT NULL,
    invoice_id uuid,
    order_id uuid,
    amount numeric(10,2) NOT NULL,
    payment_method character varying(20) NOT NULL,
    payment_preference character varying(20),
    status character varying(20) DEFAULT 'Completed'::character varying NOT NULL,
    reference_number character varying(100),
    razorpay_payment_id character varying(100),
    collected_by uuid,
    paid_at timestamp with time zone DEFAULT now() NOT NULL,
    notes text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT payments_payment_method_check CHECK (((payment_method)::text = ANY ((ARRAY['Cash'::character varying, 'UPI'::character varying, 'Card'::character varying, 'Online'::character varying, 'BankTransfer'::character varying])::text[]))),
    CONSTRAINT payments_payment_preference_check CHECK (((payment_preference)::text = ANY ((ARRAY['PerBottle'::character varying, 'Weekly'::character varying, 'Monthly'::character varying, 'Combined'::character varying])::text[]))),
    CONSTRAINT payments_status_check CHECK (((status)::text = ANY ((ARRAY['Pending'::character varying, 'Completed'::character varying, 'Failed'::character varying, 'Refunded'::character varying])::text[])))
);


--
-- Name: permissions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.permissions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    module character varying(50) NOT NULL,
    action character varying(50) NOT NULL,
    code character varying(100) NOT NULL
);


--
-- Name: plans; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.plans (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(50) NOT NULL,
    plan_type character varying(20) NOT NULL,
    monthly_price numeric(10,2) NOT NULL,
    yearly_price numeric(10,2) NOT NULL,
    max_customers integer DEFAULT 200 NOT NULL,
    max_users integer DEFAULT 3 NOT NULL,
    max_delivery_boys integer DEFAULT 1 NOT NULL,
    whatsapp_enabled boolean DEFAULT false NOT NULL,
    custom_roles_enabled boolean DEFAULT false NOT NULL,
    multi_branch_enabled boolean DEFAULT false NOT NULL,
    api_access_enabled boolean DEFAULT false NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT plans_plan_type_check CHECK (((plan_type)::text = ANY ((ARRAY['Basic'::character varying, 'Pro'::character varying, 'Enterprise'::character varying])::text[])))
);


--
-- Name: platform_billing_transactions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.platform_billing_transactions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    plan_type character varying(20) NOT NULL,
    amount numeric(10,2) NOT NULL,
    billing_cycle character varying(10) DEFAULT 'Monthly'::character varying NOT NULL,
    status character varying(20) DEFAULT 'Paid'::character varying NOT NULL,
    razorpay_payment_id character varying(100),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT platform_billing_transactions_status_check CHECK (((status)::text = ANY ((ARRAY['Paid'::character varying, 'Failed'::character varying, 'Refunded'::character varying, 'Pending'::character varying])::text[])))
);


--
-- Name: platform_users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.platform_users (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(200) NOT NULL,
    email character varying(200) NOT NULL,
    password_hash text,
    platform_role character varying(30) DEFAULT 'Support'::character varying NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    last_login_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    refresh_token text,
    refresh_token_expires_at timestamp with time zone,
    CONSTRAINT platform_users_platform_role_check CHECK (((platform_role)::text = ANY ((ARRAY['SuperAdmin'::character varying, 'Support'::character varying, 'Finance'::character varying])::text[])))
);


--
-- Name: products; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.products (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    bottle_size character varying(20) NOT NULL,
    default_rate numeric(10,2) NOT NULL,
    unit character varying(20) DEFAULT 'bottle'::character varying NOT NULL,
    hsn character varying(8),
    is_active boolean DEFAULT true NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT products_bottle_size_check CHECK (((bottle_size)::text = ANY ((ARRAY['18L'::character varying, '20L'::character varying, '250ml'::character varying, '500ml'::character varying, '1L'::character varying, 'Custom'::character varying])::text[])))
);


--
-- Name: role_permissions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.role_permissions (
    role_id uuid NOT NULL,
    permission_id uuid NOT NULL
);


--
-- Name: roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.roles (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    is_system boolean DEFAULT false NOT NULL,
    is_custom boolean DEFAULT false NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: service_requests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.service_requests (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    customer_id uuid NOT NULL,
    assigned_tech_id uuid,
    ticket_number character varying(20) NOT NULL,
    title character varying(200) NOT NULL,
    description text,
    service_type character varying(30) NOT NULL,
    status character varying(20) DEFAULT 'Open'::character varying NOT NULL,
    priority character varying(10) DEFAULT 'Medium'::character varying NOT NULL,
    scheduled_date date,
    resolved_at timestamp with time zone,
    resolution_notes text,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT service_requests_priority_check CHECK (((priority)::text = ANY ((ARRAY['Low'::character varying, 'Medium'::character varying, 'High'::character varying, 'Urgent'::character varying])::text[]))),
    CONSTRAINT service_requests_service_type_check CHECK (((service_type)::text = ANY ((ARRAY['FilterChange'::character varying, 'MembraneReplace'::character varying, 'Complaint'::character varying, 'RoutineAMC'::character varying, 'Installation'::character varying, 'Other'::character varying])::text[]))),
    CONSTRAINT service_requests_status_check CHECK (((status)::text = ANY ((ARRAY['Open'::character varying, 'InProgress'::character varying, 'Resolved'::character varying, 'Cancelled'::character varying])::text[])))
);


--
-- Name: support_tickets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.support_tickets (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    subject character varying(200) NOT NULL,
    description text,
    status character varying(20) DEFAULT 'Open'::character varying NOT NULL,
    priority character varying(20) DEFAULT 'Medium'::character varying NOT NULL,
    assigned_platform_user_id uuid,
    resolution_note text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT support_tickets_priority_check CHECK (((priority)::text = ANY ((ARRAY['Low'::character varying, 'Medium'::character varying, 'High'::character varying, 'Urgent'::character varying])::text[]))),
    CONSTRAINT support_tickets_status_check CHECK (((status)::text = ANY ((ARRAY['Open'::character varying, 'InProgress'::character varying, 'Resolved'::character varying, 'Closed'::character varying])::text[])))
);


--
-- Name: tenants; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.tenants (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    plan_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    subdomain character varying(100) NOT NULL,
    owner_name character varying(200) NOT NULL,
    owner_email character varying(200) NOT NULL,
    owner_mobile character varying(15) NOT NULL,
    logo_url text,
    primary_color character varying(7) DEFAULT '#0C447C'::character varying,
    status character varying(20) DEFAULT 'Trial'::character varying NOT NULL,
    trial_ends_at timestamp with time zone,
    subscription_ends_at timestamp with time zone,
    razorpay_subscription_id character varying(100),
    razorpay_customer_id character varying(100),
    gst_number character varying(20),
    address_line text,
    city character varying(100),
    state character varying(100),
    pincode character varying(10),
    default_language character varying(5) DEFAULT 'en'::character varying NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    subscription_discount_type character varying(20) DEFAULT 'None'::character varying NOT NULL,
    subscription_discount_value numeric(10,2) DEFAULT 0 NOT NULL,
    gst_enabled boolean DEFAULT true NOT NULL,
    gst_rate numeric(5,4) DEFAULT 0.18 NOT NULL,
    CONSTRAINT tenants_status_check CHECK (((status)::text = ANY ((ARRAY['Trial'::character varying, 'Active'::character varying, 'Suspended'::character varying, 'Overdue'::character varying, 'Cancelled'::character varying])::text[]))),
    CONSTRAINT tenants_subscription_discount_type_check CHECK (((subscription_discount_type)::text = ANY ((ARRAY['None'::character varying, 'Percentage'::character varying, 'Fixed'::character varying])::text[])))
);


--
-- Name: user_areas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_areas (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    area_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL
);


--
-- Name: users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.users (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    tenant_id uuid NOT NULL,
    role_id uuid,
    name character varying(200) NOT NULL,
    mobile character varying(15),
    email character varying(200),
    password_hash text,
    google_id character varying(200),
    google_email character varying(200),
    avatar_url text,
    auth_provider character varying(20) DEFAULT 'custom'::character varying NOT NULL,
    refresh_token text,
    device_token text,
    preferred_language character varying(5),
    is_active boolean DEFAULT true NOT NULL,
    last_login_at timestamp with time zone,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    refresh_token_expires_at timestamp with time zone,
    CONSTRAINT users_auth_provider_check CHECK (((auth_provider)::text = ANY ((ARRAY['custom'::character varying, 'google'::character varying, 'both'::character varying])::text[])))
);


--
-- Name: audit_logs_2026_06; Type: TABLE ATTACH; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs ATTACH PARTITION public.audit_logs_2026_06 FOR VALUES FROM ('2026-06-01 00:00:00+05:30') TO ('2026-07-01 00:00:00+05:30');


--
-- Name: audit_logs_2026_07; Type: TABLE ATTACH; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs ATTACH PARTITION public.audit_logs_2026_07 FOR VALUES FROM ('2026-07-01 00:00:00+05:30') TO ('2026-08-01 00:00:00+05:30');


--
-- Name: audit_logs_2026_08; Type: TABLE ATTACH; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs ATTACH PARTITION public.audit_logs_2026_08 FOR VALUES FROM ('2026-08-01 00:00:00+05:30') TO ('2026-09-01 00:00:00+05:30');


--
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");


--
-- Name: amc_subscriptions PK_amc_subscriptions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.amc_subscriptions
    ADD CONSTRAINT "PK_amc_subscriptions" PRIMARY KEY (id);


--
-- Name: user_areas PK_user_areas; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_areas
    ADD CONSTRAINT "PK_user_areas" PRIMARY KEY (id);


--
-- Name: areas areas_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.areas
    ADD CONSTRAINT areas_pkey PRIMARY KEY (id);


--
-- Name: audit_logs audit_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs
    ADD CONSTRAINT audit_logs_pkey PRIMARY KEY (id, created_at);


--
-- Name: audit_logs_2026_06 audit_logs_2026_06_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs_2026_06
    ADD CONSTRAINT audit_logs_2026_06_pkey PRIMARY KEY (id, created_at);


--
-- Name: audit_logs_2026_07 audit_logs_2026_07_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs_2026_07
    ADD CONSTRAINT audit_logs_2026_07_pkey PRIMARY KEY (id, created_at);


--
-- Name: audit_logs_2026_08 audit_logs_2026_08_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs_2026_08
    ADD CONSTRAINT audit_logs_2026_08_pkey PRIMARY KEY (id, created_at);


--
-- Name: audit_settings audit_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_settings
    ADD CONSTRAINT audit_settings_pkey PRIMARY KEY (id);


--
-- Name: customer_subscriptions customer_subscriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_subscriptions
    ADD CONSTRAINT customer_subscriptions_pkey PRIMARY KEY (id);


--
-- Name: customers customers_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT customers_pkey PRIMARY KEY (id);


--
-- Name: deliveries deliveries_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.deliveries
    ADD CONSTRAINT deliveries_pkey PRIMARY KEY (id);


--
-- Name: delivery_items delivery_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.delivery_items
    ADD CONSTRAINT delivery_items_pkey PRIMARY KEY (id);


--
-- Name: inventory_movements inventory_movements_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_movements
    ADD CONSTRAINT inventory_movements_pkey PRIMARY KEY (id);


--
-- Name: inventory inventory_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory
    ADD CONSTRAINT inventory_pkey PRIMARY KEY (id);


--
-- Name: inventory inventory_tenant_id_product_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory
    ADD CONSTRAINT inventory_tenant_id_product_id_key UNIQUE (tenant_id, product_id);


--
-- Name: invoices invoices_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT invoices_pkey PRIMARY KEY (id);


--
-- Name: notification_templates notification_templates_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_templates
    ADD CONSTRAINT notification_templates_pkey PRIMARY KEY (id);


--
-- Name: notification_templates notification_templates_tenant_id_template_code_language_cod_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_templates
    ADD CONSTRAINT notification_templates_tenant_id_template_code_language_cod_key UNIQUE (tenant_id, template_code, language_code, channel);


--
-- Name: notifications notifications_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_pkey PRIMARY KEY (id);


--
-- Name: notifications notifications_tenant_id_user_id_type_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_tenant_id_user_id_type_key UNIQUE (tenant_id, user_id, type);


--
-- Name: order_items order_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.order_items
    ADD CONSTRAINT order_items_pkey PRIMARY KEY (id);


--
-- Name: orders orders_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_pkey PRIMARY KEY (id);


--
-- Name: payments payments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_pkey PRIMARY KEY (id);


--
-- Name: permissions permissions_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.permissions
    ADD CONSTRAINT permissions_code_key UNIQUE (code);


--
-- Name: permissions permissions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.permissions
    ADD CONSTRAINT permissions_pkey PRIMARY KEY (id);


--
-- Name: plans plans_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.plans
    ADD CONSTRAINT plans_pkey PRIMARY KEY (id);


--
-- Name: platform_billing_transactions platform_billing_transactions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.platform_billing_transactions
    ADD CONSTRAINT platform_billing_transactions_pkey PRIMARY KEY (id);


--
-- Name: platform_users platform_users_email_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.platform_users
    ADD CONSTRAINT platform_users_email_key UNIQUE (email);


--
-- Name: platform_users platform_users_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.platform_users
    ADD CONSTRAINT platform_users_pkey PRIMARY KEY (id);


--
-- Name: products products_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_pkey PRIMARY KEY (id);


--
-- Name: role_permissions role_permissions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_pkey PRIMARY KEY (role_id, permission_id);


--
-- Name: roles roles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (id);


--
-- Name: service_requests service_requests_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_requests
    ADD CONSTRAINT service_requests_pkey PRIMARY KEY (id);


--
-- Name: support_tickets support_tickets_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.support_tickets
    ADD CONSTRAINT support_tickets_pkey PRIMARY KEY (id);


--
-- Name: tenants tenants_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tenants
    ADD CONSTRAINT tenants_pkey PRIMARY KEY (id);


--
-- Name: tenants tenants_subdomain_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tenants
    ADD CONSTRAINT tenants_subdomain_key UNIQUE (subdomain);


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- Name: IX_amc_subscriptions_customer_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_amc_subscriptions_customer_id" ON public.amc_subscriptions USING btree (customer_id);


--
-- Name: IX_amc_subscriptions_tenant_id_customer_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_amc_subscriptions_tenant_id_customer_id" ON public.amc_subscriptions USING btree (tenant_id, customer_id);


--
-- Name: IX_amc_subscriptions_tenant_id_next_due_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_amc_subscriptions_tenant_id_next_due_date" ON public.amc_subscriptions USING btree (tenant_id, next_due_date);


--
-- Name: IX_user_areas_area_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_user_areas_area_id" ON public.user_areas USING btree (area_id);


--
-- Name: IX_user_areas_tenant_id_area_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_user_areas_tenant_id_area_id" ON public.user_areas USING btree (tenant_id, area_id);


--
-- Name: IX_user_areas_user_id_area_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_user_areas_user_id_area_id" ON public.user_areas USING btree (user_id, area_id);


--
-- Name: idx_audit_tenant_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_audit_tenant_date ON ONLY public.audit_logs USING btree (tenant_id, created_at DESC);


--
-- Name: audit_logs_2026_06_tenant_id_created_at_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX audit_logs_2026_06_tenant_id_created_at_idx ON public.audit_logs_2026_06 USING btree (tenant_id, created_at DESC);


--
-- Name: audit_logs_2026_07_tenant_id_created_at_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX audit_logs_2026_07_tenant_id_created_at_idx ON public.audit_logs_2026_07 USING btree (tenant_id, created_at DESC);


--
-- Name: audit_logs_2026_08_tenant_id_created_at_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX audit_logs_2026_08_tenant_id_created_at_idx ON public.audit_logs_2026_08 USING btree (tenant_id, created_at DESC);


--
-- Name: audit_settings_singleton; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX audit_settings_singleton ON public.audit_settings USING btree ((true));


--
-- Name: idx_areas_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_areas_tenant ON public.areas USING btree (tenant_id) WHERE (is_deleted = false);


--
-- Name: idx_cust_subs_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_cust_subs_customer ON public.customer_subscriptions USING btree (customer_id) WHERE (is_deleted = false);


--
-- Name: idx_cust_subs_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_cust_subs_tenant ON public.customer_subscriptions USING btree (tenant_id) WHERE (is_deleted = false);


--
-- Name: idx_customers_area; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_customers_area ON public.customers USING btree (tenant_id, area_id) WHERE (is_deleted = false);


--
-- Name: idx_customers_mobile; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_customers_mobile ON public.customers USING btree (tenant_id, mobile) WHERE (is_deleted = false);


--
-- Name: idx_customers_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_customers_tenant ON public.customers USING btree (tenant_id) WHERE (is_deleted = false);


--
-- Name: idx_deliveries_boy_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_deliveries_boy_date ON public.deliveries USING btree (tenant_id, delivery_boy_id, scheduled_date);


--
-- Name: idx_deliveries_tenant_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_deliveries_tenant_date ON public.deliveries USING btree (tenant_id, scheduled_date DESC);


--
-- Name: idx_delivery_items_delivery; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_delivery_items_delivery ON public.delivery_items USING btree (delivery_id);


--
-- Name: idx_inv_movements_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inv_movements_tenant ON public.inventory_movements USING btree (tenant_id, created_at DESC);


--
-- Name: idx_inventory_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inventory_tenant ON public.inventory USING btree (tenant_id);


--
-- Name: idx_invoices_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_invoices_customer ON public.invoices USING btree (tenant_id, customer_id, status) WHERE (is_deleted = false);


--
-- Name: idx_invoices_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX idx_invoices_number ON public.invoices USING btree (tenant_id, invoice_number) WHERE (is_deleted = false);


--
-- Name: idx_invoices_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_invoices_status ON public.invoices USING btree (tenant_id, status, due_date) WHERE (is_deleted = false);


--
-- Name: idx_notif_templates_lookup; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_notif_templates_lookup ON public.notification_templates USING btree (COALESCE(tenant_id, '00000000-0000-0000-0000-000000000000'::uuid), template_code, language_code, channel);


--
-- Name: idx_notifications_unread; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_notifications_unread ON public.notifications USING btree (tenant_id, user_id, is_read);


--
-- Name: idx_order_items_order; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_order_items_order ON public.order_items USING btree (order_id);


--
-- Name: idx_orders_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_orders_customer ON public.orders USING btree (customer_id, order_date DESC) WHERE (is_deleted = false);


--
-- Name: idx_orders_delivery_boy; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_orders_delivery_boy ON public.orders USING btree (tenant_id, delivery_boy_id, order_date) WHERE (is_deleted = false);


--
-- Name: idx_orders_tenant_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_orders_tenant_date ON public.orders USING btree (tenant_id, order_date DESC) WHERE (is_deleted = false);


--
-- Name: idx_payments_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_payments_customer ON public.payments USING btree (tenant_id, customer_id, paid_at DESC);


--
-- Name: idx_payments_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_payments_tenant ON public.payments USING btree (tenant_id, paid_at DESC);


--
-- Name: idx_platform_billing_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_platform_billing_status ON public.platform_billing_transactions USING btree (status);


--
-- Name: idx_platform_billing_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_platform_billing_tenant ON public.platform_billing_transactions USING btree (tenant_id);


--
-- Name: idx_products_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_tenant ON public.products USING btree (tenant_id) WHERE (is_deleted = false);


--
-- Name: idx_roles_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_roles_tenant ON public.roles USING btree (tenant_id) WHERE (is_deleted = false);


--
-- Name: idx_sr_tech; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_sr_tech ON public.service_requests USING btree (tenant_id, assigned_tech_id, status) WHERE (is_deleted = false);


--
-- Name: idx_sr_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_sr_tenant ON public.service_requests USING btree (tenant_id, status) WHERE (is_deleted = false);


--
-- Name: idx_support_tickets_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_support_tickets_status ON public.support_tickets USING btree (status);


--
-- Name: idx_support_tickets_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_support_tickets_tenant ON public.support_tickets USING btree (tenant_id);


--
-- Name: idx_tenants_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_tenants_status ON public.tenants USING btree (status) WHERE (is_deleted = false);


--
-- Name: idx_tenants_subdomain; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX idx_tenants_subdomain ON public.tenants USING btree (subdomain) WHERE (is_deleted = false);


--
-- Name: idx_users_email_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX idx_users_email_tenant ON public.users USING btree (tenant_id, email) WHERE ((is_deleted = false) AND (email IS NOT NULL));


--
-- Name: idx_users_google; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX idx_users_google ON public.users USING btree (google_id) WHERE (google_id IS NOT NULL);


--
-- Name: idx_users_tenant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_users_tenant ON public.users USING btree (tenant_id) WHERE (is_deleted = false);


--
-- Name: audit_logs_2026_06_pkey; Type: INDEX ATTACH; Schema: public; Owner: -
--

ALTER INDEX public.audit_logs_pkey ATTACH PARTITION public.audit_logs_2026_06_pkey;


--
-- Name: audit_logs_2026_06_tenant_id_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: -
--

ALTER INDEX public.idx_audit_tenant_date ATTACH PARTITION public.audit_logs_2026_06_tenant_id_created_at_idx;


--
-- Name: audit_logs_2026_07_pkey; Type: INDEX ATTACH; Schema: public; Owner: -
--

ALTER INDEX public.audit_logs_pkey ATTACH PARTITION public.audit_logs_2026_07_pkey;


--
-- Name: audit_logs_2026_07_tenant_id_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: -
--

ALTER INDEX public.idx_audit_tenant_date ATTACH PARTITION public.audit_logs_2026_07_tenant_id_created_at_idx;


--
-- Name: audit_logs_2026_08_pkey; Type: INDEX ATTACH; Schema: public; Owner: -
--

ALTER INDEX public.audit_logs_pkey ATTACH PARTITION public.audit_logs_2026_08_pkey;


--
-- Name: audit_logs_2026_08_tenant_id_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: -
--

ALTER INDEX public.idx_audit_tenant_date ATTACH PARTITION public.audit_logs_2026_08_tenant_id_created_at_idx;


--
-- Name: amc_subscriptions FK_amc_subscriptions_customers_customer_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.amc_subscriptions
    ADD CONSTRAINT "FK_amc_subscriptions_customers_customer_id" FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: user_areas FK_user_areas_areas_area_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_areas
    ADD CONSTRAINT "FK_user_areas_areas_area_id" FOREIGN KEY (area_id) REFERENCES public.areas(id) ON DELETE CASCADE;


--
-- Name: user_areas FK_user_areas_users_user_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_areas
    ADD CONSTRAINT "FK_user_areas_users_user_id" FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: areas areas_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.areas
    ADD CONSTRAINT areas_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: audit_logs audit_logs_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.audit_logs
    ADD CONSTRAINT audit_logs_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id);


--
-- Name: audit_logs audit_logs_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.audit_logs
    ADD CONSTRAINT audit_logs_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id);


--
-- Name: customer_subscriptions customer_subscriptions_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_subscriptions
    ADD CONSTRAINT customer_subscriptions_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id);


--
-- Name: customer_subscriptions customer_subscriptions_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_subscriptions
    ADD CONSTRAINT customer_subscriptions_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id);


--
-- Name: customer_subscriptions customer_subscriptions_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_subscriptions
    ADD CONSTRAINT customer_subscriptions_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: customers customers_area_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT customers_area_id_fkey FOREIGN KEY (area_id) REFERENCES public.areas(id);


--
-- Name: customers customers_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT customers_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: deliveries deliveries_delivery_boy_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.deliveries
    ADD CONSTRAINT deliveries_delivery_boy_id_fkey FOREIGN KEY (delivery_boy_id) REFERENCES public.users(id);


--
-- Name: deliveries deliveries_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.deliveries
    ADD CONSTRAINT deliveries_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(id);


--
-- Name: deliveries deliveries_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.deliveries
    ADD CONSTRAINT deliveries_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: delivery_items delivery_items_delivery_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.delivery_items
    ADD CONSTRAINT delivery_items_delivery_id_fkey FOREIGN KEY (delivery_id) REFERENCES public.deliveries(id) ON DELETE CASCADE;


--
-- Name: delivery_items delivery_items_order_item_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.delivery_items
    ADD CONSTRAINT delivery_items_order_item_id_fkey FOREIGN KEY (order_item_id) REFERENCES public.order_items(id);


--
-- Name: delivery_items delivery_items_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.delivery_items
    ADD CONSTRAINT delivery_items_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id);


--
-- Name: delivery_items delivery_items_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.delivery_items
    ADD CONSTRAINT delivery_items_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: inventory_movements inventory_movements_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_movements
    ADD CONSTRAINT inventory_movements_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id);


--
-- Name: inventory_movements inventory_movements_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_movements
    ADD CONSTRAINT inventory_movements_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(id);


--
-- Name: inventory_movements inventory_movements_performed_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_movements
    ADD CONSTRAINT inventory_movements_performed_by_fkey FOREIGN KEY (performed_by) REFERENCES public.users(id);


--
-- Name: inventory_movements inventory_movements_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_movements
    ADD CONSTRAINT inventory_movements_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id);


--
-- Name: inventory_movements inventory_movements_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_movements
    ADD CONSTRAINT inventory_movements_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: inventory inventory_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory
    ADD CONSTRAINT inventory_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id);


--
-- Name: inventory inventory_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory
    ADD CONSTRAINT inventory_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: invoices invoices_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT invoices_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id);


--
-- Name: invoices invoices_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT invoices_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: notification_templates notification_templates_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_templates
    ADD CONSTRAINT notification_templates_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: notifications notifications_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: notifications notifications_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: order_items order_items_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.order_items
    ADD CONSTRAINT order_items_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(id) ON DELETE CASCADE;


--
-- Name: order_items order_items_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.order_items
    ADD CONSTRAINT order_items_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id);


--
-- Name: order_items order_items_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.order_items
    ADD CONSTRAINT order_items_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: orders orders_area_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_area_id_fkey FOREIGN KEY (area_id) REFERENCES public.areas(id);


--
-- Name: orders orders_created_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.users(id);


--
-- Name: orders orders_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id);


--
-- Name: orders orders_delivery_boy_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_delivery_boy_id_fkey FOREIGN KEY (delivery_boy_id) REFERENCES public.users(id);


--
-- Name: orders orders_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: payments payments_collected_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_collected_by_fkey FOREIGN KEY (collected_by) REFERENCES public.users(id);


--
-- Name: payments payments_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id);


--
-- Name: payments payments_invoice_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_invoice_id_fkey FOREIGN KEY (invoice_id) REFERENCES public.invoices(id);


--
-- Name: payments payments_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(id);


--
-- Name: payments payments_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: platform_billing_transactions platform_billing_transactions_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.platform_billing_transactions
    ADD CONSTRAINT platform_billing_transactions_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: products products_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: role_permissions role_permissions_permission_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_permission_id_fkey FOREIGN KEY (permission_id) REFERENCES public.permissions(id) ON DELETE CASCADE;


--
-- Name: role_permissions role_permissions_role_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_role_id_fkey FOREIGN KEY (role_id) REFERENCES public.roles(id) ON DELETE CASCADE;


--
-- Name: roles roles_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: service_requests service_requests_assigned_tech_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_requests
    ADD CONSTRAINT service_requests_assigned_tech_id_fkey FOREIGN KEY (assigned_tech_id) REFERENCES public.users(id);


--
-- Name: service_requests service_requests_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_requests
    ADD CONSTRAINT service_requests_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id);


--
-- Name: service_requests service_requests_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_requests
    ADD CONSTRAINT service_requests_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: support_tickets support_tickets_assigned_platform_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.support_tickets
    ADD CONSTRAINT support_tickets_assigned_platform_user_id_fkey FOREIGN KEY (assigned_platform_user_id) REFERENCES public.platform_users(id) ON DELETE SET NULL;


--
-- Name: support_tickets support_tickets_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.support_tickets
    ADD CONSTRAINT support_tickets_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: tenants tenants_plan_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tenants
    ADD CONSTRAINT tenants_plan_id_fkey FOREIGN KEY (plan_id) REFERENCES public.plans(id);


--
-- Name: users users_role_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_role_id_fkey FOREIGN KEY (role_id) REFERENCES public.roles(id);


--
-- Name: users users_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: customers; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.customers ENABLE ROW LEVEL SECURITY;

--
-- Name: deliveries; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.deliveries ENABLE ROW LEVEL SECURITY;

--
-- Name: invoices; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.invoices ENABLE ROW LEVEL SECURITY;

--
-- Name: orders; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.orders ENABLE ROW LEVEL SECURITY;

--
-- Name: payments; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payments ENABLE ROW LEVEL SECURITY;

--
-- Name: customers tenant_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY tenant_isolation ON public.customers USING ((tenant_id = (current_setting('app.current_tenant_id'::text, true))::uuid));


--
-- Name: deliveries tenant_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY tenant_isolation ON public.deliveries USING ((tenant_id = (current_setting('app.current_tenant_id'::text, true))::uuid));


--
-- Name: invoices tenant_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY tenant_isolation ON public.invoices USING ((tenant_id = (current_setting('app.current_tenant_id'::text, true))::uuid));


--
-- Name: orders tenant_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY tenant_isolation ON public.orders USING ((tenant_id = (current_setting('app.current_tenant_id'::text, true))::uuid));


--
-- Name: payments tenant_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY tenant_isolation ON public.payments USING ((tenant_id = (current_setting('app.current_tenant_id'::text, true))::uuid));


--
-- PostgreSQL database dump complete
--




-- ─────────────────────────────────────────────────────────────────────────────
-- The schema dump left search_path empty; restore it for the sections below.
-- ─────────────────────────────────────────────────────────────────────────────
SET search_path TO public, extensions;


-- ─────────────────────────────────────────────────────────────────────────────
-- APP ROLE & GRANTS — the API connects as this NON-owner role so RLS applies.
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'rocloud_app') THEN
        CREATE ROLE rocloud_app LOGIN PASSWORD 'CHANGE_ME_STRONG_PASSWORD';   -- ← EDIT THIS
    END IF;
END
$$;

GRANT USAGE ON SCHEMA public TO rocloud_app;
GRANT USAGE ON SCHEMA extensions TO rocloud_app;
GRANT ALL PRIVILEGES ON ALL TABLES    IN SCHEMA public TO rocloud_app;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO rocloud_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL PRIVILEGES ON TABLES    TO rocloud_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL PRIVILEGES ON SEQUENCES TO rocloud_app;

-- Hangfire installs its own tables on first run; give the role its own schema.
CREATE SCHEMA IF NOT EXISTS hangfire AUTHORIZATION rocloud_app;

-- audit_logs is append-only: INSERT/SELECT only, never UPDATE/DELETE.
REVOKE UPDATE, DELETE, TRUNCATE ON audit_logs FROM rocloud_app;
GRANT  INSERT, SELECT             ON audit_logs TO   rocloud_app;

-- Supabase hardening: by default Supabase exposes public tables through its auto REST API
-- (anon/authenticated roles). This app is NOT used via PostgREST, and not every table has RLS,
-- so lock those roles out of our schema to avoid accidental data exposure.
DO $$
BEGIN
    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'anon') THEN
        EXECUTE 'REVOKE ALL ON ALL TABLES IN SCHEMA public FROM anon, authenticated';
        EXECUTE 'REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM anon, authenticated';
        EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM anon, authenticated';
        EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM anon, authenticated';
    END IF;
END
$$;


-- ─────────────────────────────────────────────────────────────────────────────
-- REFERENCE DATA — mirrors the live plans/permissions so registration works.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO permissions (module, action, code) VALUES
  ('AMC', 'Manage', 'AMC.Manage'),
  ('AMC', 'Update', 'AMC.Update'),
  ('AMC', 'View', 'AMC.View'),
  ('Customers', 'Create', 'Customers.Create'),
  ('Customers', 'Delete', 'Customers.Delete'),
  ('Customers', 'Edit', 'Customers.Edit'),
  ('Customers', 'View', 'Customers.View'),
  ('Deliveries', 'Update', 'Deliveries.Update'),
  ('Deliveries', 'View', 'Deliveries.View'),
  ('Deliveries', 'ViewOwn', 'Deliveries.ViewOwn'),
  ('Inventory', 'Manage', 'Inventory.Manage'),
  ('Inventory', 'View', 'Inventory.View'),
  ('Invoices', 'Create', 'Invoices.Create'),
  ('Invoices', 'Edit', 'Invoices.Edit'),
  ('Invoices', 'View', 'Invoices.View'),
  ('Orders', 'Cancel', 'Orders.Cancel'),
  ('Orders', 'Create', 'Orders.Create'),
  ('Orders', 'Edit', 'Orders.Edit'),
  ('Orders', 'View', 'Orders.View'),
  ('Payments', 'Collect', 'Payments.Collect'),
  ('Payments', 'Manage', 'Payments.Manage'),
  ('Payments', 'View', 'Payments.View'),
  ('Reports', 'View', 'Reports.View'),
  ('Roles', 'Manage', 'Roles.Manage'),
  ('Roles', 'View', 'Roles.View'),
  -- One View/Manage pair per settings PAGE. The tenant's own ROCloud subscription has no permission
  -- at all — it is [RequireOwner], so no custom role can be granted the right to change the plan.
  ('Areas', 'View', 'Areas.View'),
  ('Areas', 'Manage', 'Areas.Manage'),
  ('Notifications', 'View', 'Notifications.View'),
  ('Notifications', 'Manage', 'Notifications.Manage'),
  ('Business Profile', 'View', 'BusinessProfile.View'),
  ('Business Profile', 'Manage', 'BusinessProfile.Manage'),
  ('Users', 'Manage', 'Users.Manage'),
  ('Users', 'View', 'Users.View')
ON CONFLICT (code) DO NOTHING;

-- max_customers / max_users / max_delivery_boys: 0 = unlimited (Plan.Unlimited).
-- whatsapp_enabled, multi_branch_enabled, api_access_enabled are false on every plan:
-- those features are not built yet and both portals render them as "coming soon".
INSERT INTO plans (name, plan_type, monthly_price, yearly_price, max_customers, max_users,
                   max_delivery_boys, whatsapp_enabled, custom_roles_enabled, multi_branch_enabled,
                   api_access_enabled, is_active)
SELECT * FROM (VALUES
  ('Basic',      'Basic',      1099.00,  9990.00, 200,  3, 1, false, false, false, false, true),
  ('Pro',        'Pro',        2499.00, 24990.00, 1000, 10, 5, false, false, false, false, true),
  ('Enterprise', 'Enterprise', 5999.00, 59990.00, 0,   0, 0, false, true,  false, false, true)
) AS v(name, plan_type, monthly_price, yearly_price, max_customers, max_users,
       max_delivery_boys, whatsapp_enabled, custom_roles_enabled, multi_branch_enabled,
       api_access_enabled, is_active)
WHERE NOT EXISTS (SELECT 1 FROM plans);

-- Activity-log settings: ensure the single global row exists.
INSERT INTO audit_settings (id)
SELECT gen_random_uuid()
WHERE NOT EXISTS (SELECT 1 FROM audit_settings);


-- Sanity check.
SELECT
  (SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public') AS tables,
  (SELECT count(*) FROM pg_policies WHERE schemaname = 'public')                 AS rls_policies,
  (SELECT count(*) FROM plans)                                                   AS plans,
  (SELECT count(*) FROM permissions)                                             AS permissions,
  (SELECT count(*) FROM audit_settings)                                          AS audit_settings;
