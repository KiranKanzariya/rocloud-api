-- ─────────────────────────────────────────────────────────────────────────────
-- Migration: seed system-default notification templates (tenant_id = NULL)
-- ─────────────────────────────────────────────────────────────────────────────
-- These NULL-tenant rows are the shared defaults the send path renders from
-- (INotificationTemplateRenderer). A tenant may later override any of them with a
-- row carrying their own tenant_id. Covers en/hi/gu for the four wired events:
--   invoice_sent        (Email)     tokens: {{CustomerName}} {{InvoiceNumber}} {{DownloadUrl}}
--   payment_reminder    (WhatsApp)  tokens: {{CustomerName}} {{Amount}} {{DaysOverdue}}
--   amc_reminder        (WhatsApp)  tokens: {{CustomerName}} {{TicketNumber}} {{ScheduledDate}}
--   subscription_expiry (Email)     tokens: {{TenantName}} {{Days}}
--
-- Idempotent: re-running inserts only the rows that are still missing (the UNIQUE
-- constraint treats NULL tenant_id as distinct, so we guard with NOT EXISTS rather
-- than ON CONFLICT). Safe to run on dev, prod, and Supabase.
-- ─────────────────────────────────────────────────────────────────────────────

INSERT INTO public.notification_templates
    (id, tenant_id, template_code, language_code, channel, subject, body, created_at, updated_at)
SELECT gen_random_uuid(), NULL, v.code, v.lang, v.channel, v.subject, v.body, now(), now()
FROM (VALUES
    -- ── invoice_sent (Email — PDF is attached to the mail, not linked) ───────
    ('invoice_sent', 'en', 'Email',
        'Invoice {{InvoiceNumber}}',
        'Hi {{CustomerName}}, your invoice {{InvoiceNumber}} is attached to this email. Thank you.'),
    ('invoice_sent', 'hi', 'Email',
        'इनवॉइस {{InvoiceNumber}}',
        'नमस्ते {{CustomerName}}, आपका इनवॉइस {{InvoiceNumber}} इस ईमेल के साथ संलग्न है। धन्यवाद।'),
    ('invoice_sent', 'gu', 'Email',
        'ઇન્વોઇસ {{InvoiceNumber}}',
        'નમસ્તે {{CustomerName}}, તમારું ઇન્વોઇસ {{InvoiceNumber}} આ ઈમેલ સાથે જોડેલું છે. આભાર.'),

    -- ── payment_reminder (WhatsApp, no subject) ─────────────────────────────
    ('payment_reminder', 'en', 'WhatsApp', NULL,
        'Hi {{CustomerName}}, you have ₹{{Amount}} outstanding ({{DaysOverdue}} days overdue). Please clear your dues. Thank you.'),
    ('payment_reminder', 'hi', 'WhatsApp', NULL,
        'नमस्ते {{CustomerName}}, आपके ₹{{Amount}} बकाया हैं ({{DaysOverdue}} दिन से)। कृपया अपना भुगतान करें। धन्यवाद।'),
    ('payment_reminder', 'gu', 'WhatsApp', NULL,
        'નમસ્તે {{CustomerName}}, તમારા ₹{{Amount}} બાકી છે ({{DaysOverdue}} દિવસથી). કૃપા કરીને ચુકવણી કરો. આભાર.'),

    -- ── amc_reminder (WhatsApp, no subject) ─────────────────────────────────
    ('amc_reminder', 'en', 'WhatsApp', NULL,
        'Hi {{CustomerName}}, your service visit ({{TicketNumber}}) is scheduled for {{ScheduledDate}}. Our technician will reach you. Thank you.'),
    ('amc_reminder', 'hi', 'WhatsApp', NULL,
        'नमस्ते {{CustomerName}}, आपकी सर्विस विज़िट ({{TicketNumber}}) {{ScheduledDate}} को निर्धारित है। हमारा तकनीशियन आपके पास पहुंचेगा। धन्यवाद।'),
    ('amc_reminder', 'gu', 'WhatsApp', NULL,
        'નમસ્તે {{CustomerName}}, તમારી સર્વિસ મુલાકાત ({{TicketNumber}}) {{ScheduledDate}} ના રોજ નિર્ધારિત છે. અમારો ટેકનિશિયન તમારી પાસે પહોંચશે. આભાર.'),

    -- ── subscription_expiry (Email) ─────────────────────────────────────────
    ('subscription_expiry', 'en', 'Email',
        'Your ROCloud subscription is expiring soon',
        'Hi {{TenantName}}, your ROCloud subscription expires within {{Days}} days. Please renew to avoid interruption.'),
    ('subscription_expiry', 'hi', 'Email',
        'आपकी ROCloud सदस्यता जल्द समाप्त हो रही है',
        'नमस्ते {{TenantName}}, आपकी ROCloud सदस्यता {{Days}} दिनों में समाप्त हो रही है। रुकावट से बचने के लिए कृपया नवीनीकरण करें।'),
    ('subscription_expiry', 'gu', 'Email',
        'તમારું ROCloud સબ્સ્ક્રિપ્શન ટૂંક સમયમાં સમાપ્ત થઈ રહ્યું છે',
        'નમસ્તે {{TenantName}}, તમારું ROCloud સબ્સ્ક્રિપ્શન {{Days}} દિવસમાં સમાપ્ત થાય છે. વિક્ષેપ ટાળવા કૃપા કરીને નવીકરણ કરો.')
) AS v(code, lang, channel, subject, body)
WHERE NOT EXISTS (
    SELECT 1 FROM public.notification_templates t
    WHERE t.tenant_id IS NULL
      AND t.template_code = v.code
      AND t.language_code = v.lang
      AND t.channel = v.channel
);
