-- ─────────────────────────────────────────────────────────────────────────────
-- Migration: seed Email-channel notification templates for the reminder events
-- ─────────────────────────────────────────────────────────────────────────────
-- The reminder jobs (PaymentReminderJob / AmcReminderJob / AdvanceOrderReminderJob)
-- deliver by WhatsApp when the tenant's plan includes it and the customer has a
-- mobile; otherwise they fall back to EMAIL when the customer has an email on file.
-- These NULL-tenant Email rows give that fallback a localised, owner-editable body
-- (INotificationTemplateRenderer); a tenant may override any of them. The jobs still
-- carry a hardcoded English body/subject, so this is optional for en but required for
-- hi/gu customers to get a localised fallback mail. Subjects mirror the jobs' fallbacks.
--   payment_reminder       (Email)  tokens: {{CustomerName}} {{Amount}} {{DaysOverdue}}
--   amc_reminder           (Email)  tokens: {{CustomerName}} {{TicketNumber}} {{ScheduledDate}}
--   advance_order_reminder (Email)  tokens: {{CustomerName}} {{ScheduledDate}} {{Quantity}}
--
-- Idempotent: re-running inserts only the rows that are still missing (the UNIQUE
-- constraint treats NULL tenant_id as distinct, so we guard with NOT EXISTS rather
-- than ON CONFLICT). Safe to run on dev, prod, and Supabase.
-- ─────────────────────────────────────────────────────────────────────────────

INSERT INTO public.notification_templates
    (id, tenant_id, template_code, language_code, channel, subject, body, created_at, updated_at)
SELECT gen_random_uuid(), NULL, v.code, v.lang, v.channel, v.subject, v.body, now(), now()
FROM (VALUES
    -- ── payment_reminder (Email) ────────────────────────────────────────────
    ('payment_reminder', 'en', 'Email',
        'Payment reminder',
        'Hi {{CustomerName}}, you have ₹{{Amount}} outstanding ({{DaysOverdue}} days overdue). Please clear your dues. Thank you.'),
    ('payment_reminder', 'hi', 'Email',
        'भुगतान अनुस्मारक',
        'नमस्ते {{CustomerName}}, आपके ₹{{Amount}} बकाया हैं ({{DaysOverdue}} दिन से)। कृपया अपना भुगतान करें। धन्यवाद।'),
    ('payment_reminder', 'gu', 'Email',
        'ચુકવણી રિમાઇન્ડર',
        'નમસ્તે {{CustomerName}}, તમારા ₹{{Amount}} બાકી છે ({{DaysOverdue}} દિવસથી). કૃપા કરીને ચુકવણી કરો. આભાર.'),

    -- ── amc_reminder (Email) ────────────────────────────────────────────────
    ('amc_reminder', 'en', 'Email',
        'Upcoming service visit reminder',
        'Hi {{CustomerName}}, your service visit ({{TicketNumber}}) is scheduled for {{ScheduledDate}}. Our technician will reach you. Thank you.'),
    ('amc_reminder', 'hi', 'Email',
        'आगामी सर्विस विज़िट अनुस्मारक',
        'नमस्ते {{CustomerName}}, आपकी सर्विस विज़िट ({{TicketNumber}}) {{ScheduledDate}} को निर्धारित है। हमारा तकनीशियन आपके पास पहुंचेगा। धन्यवाद।'),
    ('amc_reminder', 'gu', 'Email',
        'આગામી સર્વિસ મુલાકાત રિમાઇન્ડર',
        'નમસ્તે {{CustomerName}}, તમારી સર્વિસ મુલાકાત ({{TicketNumber}}) {{ScheduledDate}} ના રોજ નિર્ધારિત છે. અમારો ટેકનિશિયન તમારી પાસે પહોંચશે. આભાર.'),

    -- ── advance_order_reminder (Email) ──────────────────────────────────────
    ('advance_order_reminder', 'en', 'Email',
        'Upcoming order reminder',
        'Hi {{CustomerName}}, a reminder that your order of {{Quantity}} item(s) is scheduled for {{ScheduledDate}}. We''ll have it ready. Thank you.'),
    ('advance_order_reminder', 'hi', 'Email',
        'आगामी ऑर्डर अनुस्मारक',
        'नमस्ते {{CustomerName}}, याद दिलाना चाहते हैं कि आपका {{Quantity}} वस्तु का ऑर्डर {{ScheduledDate}} को निर्धारित है। हम इसे तैयार रखेंगे। धन्यवाद।'),
    ('advance_order_reminder', 'gu', 'Email',
        'આગામી ઓર્ડર રિમાઇન્ડર',
        'નમસ્તે {{CustomerName}}, યાદ અપાવીએ છીએ કે તમારો {{Quantity}} વસ્તુનો ઓર્ડર {{ScheduledDate}} ના રોજ નિર્ધારિત છે. અમે તે તૈયાર રાખીશું. આભાર.')
) AS v(code, lang, channel, subject, body)
WHERE NOT EXISTS (
    SELECT 1 FROM public.notification_templates t
    WHERE t.tenant_id IS NULL
      AND t.template_code = v.code
      AND t.language_code = v.lang
      AND t.channel = v.channel
);
