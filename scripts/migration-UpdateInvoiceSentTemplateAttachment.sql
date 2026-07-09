-- ─────────────────────────────────────────────────────────────────────────────
-- Migration: invoice_sent email now carries the PDF as an ATTACHMENT, not a link
-- ─────────────────────────────────────────────────────────────────────────────
-- The send path (SendInvoiceCommand) now attaches the invoice PDF to the email.
-- This rewrites the SYSTEM-DEFAULT invoice_sent bodies (tenant_id IS NULL) so they
-- reference the attachment instead of the old <a href="{{DownloadUrl}}"> link.
--
-- Tenant OVERRIDES (rows with a tenant_id) are intentionally left untouched — a
-- tenant that customised their template still owns it, and the PDF is attached
-- regardless of what any template body says.
--
-- Idempotent: re-running sets the same text. notification_templates is app-owned,
-- so no privileged role is needed. Safe to run on dev, prod, and Supabase.
-- ─────────────────────────────────────────────────────────────────────────────

UPDATE public.notification_templates
SET body = CASE language_code
        WHEN 'en' THEN 'Hi {{CustomerName}}, your invoice {{InvoiceNumber}} is attached to this email. Thank you.'
        WHEN 'hi' THEN 'नमस्ते {{CustomerName}}, आपका इनवॉइस {{InvoiceNumber}} इस ईमेल के साथ संलग्न है। धन्यवाद।'
        WHEN 'gu' THEN 'નમસ્તે {{CustomerName}}, તમારું ઇન્વોઇસ {{InvoiceNumber}} આ ઈમેલ સાથે જોડેલું છે. આભાર.'
        ELSE body
    END,
    updated_at = now()
WHERE tenant_id IS NULL
  AND template_code = 'invoice_sent'
  AND channel = 'Email'
  AND language_code IN ('en', 'hi', 'gu');
