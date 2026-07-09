-- ─────────────────────────────────────────────────────────────────────────────
-- Migration: add the reminder_log table (reminder throttling)
-- ─────────────────────────────────────────────────────────────────────────────
-- The recurring reminder jobs (PaymentReminderJob / AmcReminderJob / AdvanceOrderReminderJob) log
-- each reminder they actually send here, then skip a subject that was reminded within the configured
-- interval (Jobs:*MinIntervalDays) — so they stop re-sending on every run. Insert-only; created_at is
-- the sent time. App-owned like notifications (tenant isolation is enforced by the app-level query
-- filter, so no RLS policy is needed) — a plain CREATE TABLE by the app role is enough.
--
-- Idempotent (CREATE TABLE / INDEX IF NOT EXISTS). Safe to run on dev, prod, and Supabase.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS public.reminder_log (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    tenant_id uuid NOT NULL,
    reminder_type character varying(40) NOT NULL,
    subject_id uuid NOT NULL,
    customer_id uuid,
    channel character varying(20) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT reminder_log_pkey PRIMARY KEY (id),
    CONSTRAINT reminder_log_tenant_id_fkey FOREIGN KEY (tenant_id)
        REFERENCES public.tenants(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_reminder_log_lookup
    ON public.reminder_log USING btree (tenant_id, reminder_type, subject_id, created_at);
