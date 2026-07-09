-- ─────────────────────────────────────────────────────────────────────────────
-- Migration: seed the advance_order_reminder notification template (tenant_id = NULL)
-- ─────────────────────────────────────────────────────────────────────────────
-- The day-before WhatsApp reminder for Advance (event/program) bookings. NULL-tenant rows are the
-- shared defaults the send path renders from (INotificationTemplateRenderer); a tenant may override.
--   advance_order_reminder (WhatsApp) tokens: {{CustomerName}} {{ScheduledDate}} {{Quantity}}
--
-- The AdvanceOrderReminderJob has a hardcoded English fallback, so this is optional for en but
-- required for hi/gu customers to get a localised message.
--
-- Idempotent (guards with NOT EXISTS; NULL tenant_id is distinct so ON CONFLICT won't fire).
-- Safe to run on dev, prod, and Supabase.
-- ─────────────────────────────────────────────────────────────────────────────

INSERT INTO public.notification_templates
    (id, tenant_id, template_code, language_code, channel, subject, body, created_at, updated_at)
SELECT gen_random_uuid(), NULL, v.code, v.lang, v.channel, v.subject, v.body, now(), now()
FROM (VALUES
    ('advance_order_reminder', 'en', 'WhatsApp', NULL,
        'Hi {{CustomerName}}, a reminder that your order of {{Quantity}} item(s) is scheduled for {{ScheduledDate}}. We''ll have it ready. Thank you.'),
    ('advance_order_reminder', 'hi', 'WhatsApp', NULL,
        'नमस्ते {{CustomerName}}, याद दिलाना चाहते हैं कि आपका {{Quantity}} वस्तु का ऑर्डर {{ScheduledDate}} को निर्धारित है। हम इसे तैयार रखेंगे। धन्यवाद।'),
    ('advance_order_reminder', 'gu', 'WhatsApp', NULL,
        'નમસ્તે {{CustomerName}}, યાદ અપાવીએ છીએ કે તમારો {{Quantity}} વસ્તુનો ઓર્ડર {{ScheduledDate}} ના રોજ નિર્ધારિત છે. અમે તે તૈયાર રાખીશું. આભાર.')
) AS v(code, lang, channel, subject, body)
WHERE NOT EXISTS (
    SELECT 1 FROM public.notification_templates t
    WHERE t.tenant_id IS NULL
      AND t.template_code = v.code
      AND t.language_code = v.lang
      AND t.channel = v.channel
);
