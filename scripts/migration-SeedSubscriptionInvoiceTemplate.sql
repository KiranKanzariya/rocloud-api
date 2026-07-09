-- ─────────────────────────────────────────────────────────────────────────────
-- Migration: seed the subscription_invoice email template (platform → tenant, tenant_id = NULL)
-- ─────────────────────────────────────────────────────────────────────────────
-- The "please pay" mail sent to the tenant owner when a Pending ROCloud subscription invoice is
-- raised (SubscriptionInvoiceDelivery). NULL-tenant rows are the shared defaults the send path
-- renders from (INotificationTemplateRenderer); admins may edit these platform defaults. Platform
-- billing mail is ROCloud-branded (not the tenant business), like subscription_expiry.
--   subscription_invoice (Email) tokens: {{TenantName}} {{InvoiceNumber}} {{PlanName}} {{Amount}} {{DueDate}} {{PayUrl}}
--
-- Idempotent: NOT EXISTS guard (NULL tenant_id is distinct, so ON CONFLICT won't fire).
-- Safe to run on dev, prod, and Supabase.
-- ─────────────────────────────────────────────────────────────────────────────

INSERT INTO public.notification_templates
    (id, tenant_id, template_code, language_code, channel, subject, body, created_at, updated_at)
SELECT gen_random_uuid(), NULL, v.code, v.lang, v.channel, v.subject, v.body, now(), now()
FROM (VALUES
    ('subscription_invoice', 'en', 'Email',
        'Your ROCloud invoice {{InvoiceNumber}}',
        'Hi {{TenantName}}, your ROCloud {{PlanName}} invoice {{InvoiceNumber}} for ₹{{Amount}} is attached. Please pay by {{DueDate}} to keep your service active. Pay here: {{PayUrl}}'),
    ('subscription_invoice', 'hi', 'Email',
        'आपका ROCloud इनवॉइस {{InvoiceNumber}}',
        'नमस्ते {{TenantName}}, आपका ROCloud {{PlanName}} इनवॉइस {{InvoiceNumber}} (₹{{Amount}}) संलग्न है। सेवा जारी रखने के लिए कृपया {{DueDate}} तक भुगतान करें। यहाँ भुगतान करें: {{PayUrl}}'),
    ('subscription_invoice', 'gu', 'Email',
        'તમારું ROCloud ઇન્વોઇસ {{InvoiceNumber}}',
        'નમસ્તે {{TenantName}}, તમારું ROCloud {{PlanName}} ઇન્વોઇસ {{InvoiceNumber}} (₹{{Amount}}) જોડેલું છે. સેવા ચાલુ રાખવા કૃપા કરીને {{DueDate}} સુધીમાં ચુકવણી કરો. અહીં ચુકવણી કરો: {{PayUrl}}')
) AS v(code, lang, channel, subject, body)
WHERE NOT EXISTS (
    SELECT 1 FROM public.notification_templates t
    WHERE t.tenant_id IS NULL
      AND t.template_code = v.code
      AND t.language_code = v.lang
      AND t.channel = v.channel
);
