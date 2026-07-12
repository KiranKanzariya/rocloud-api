-- ─────────────────────────────────────────────────────────────────────────────
-- Migration: seed the remaining platform → tenant-owner email templates (tenant_id = NULL)
-- ─────────────────────────────────────────────────────────────────────────────
-- These four were hardcoded English strings inside the C# handlers. Seeding them makes the copy
-- localised (en/hi/gu) and editable from the super-admin portal, like subscription_expiry /
-- subscription_invoice. Each handler keeps its original text as a built-in fallback, so a missing
-- row never breaks a send. ROCloud-branded (platform mail), not the tenant's business.
--
--   welcome              (Email)  tokens: {{OwnerName}} {{LoginUrl}} {{TrialEndsAt}}
--   welcome_google       (Email)  tokens: {{OwnerName}} {{LoginUrl}} {{TrialEndsAt}}
--   password_reset       (Email)  tokens: {{Name}} {{ResetUrl}} {{Minutes}}
--   subscription_receipt (Email)  tokens: {{TenantName}} {{InvoiceNumber}} {{Amount}}
--
-- NOTE: password_reset contains an <a href="{{ResetUrl}}"> link — the reset page reads the token
-- from ?token=, so a bare token is useless to the owner. The admin editor sanitises saved HTML
-- (anchors allowed, javascript: stripped); the renderer only substitutes tokens.
--
-- Idempotent: NOT EXISTS guard (NULL tenant_id is distinct, so ON CONFLICT won't fire).
-- Safe to run on dev, prod, and Supabase.
-- ─────────────────────────────────────────────────────────────────────────────

INSERT INTO public.notification_templates
    (id, tenant_id, template_code, language_code, channel, subject, body, created_at, updated_at)
SELECT gen_random_uuid(), NULL, v.code, v.lang, v.channel, v.subject, v.body, now(), now()
FROM (VALUES
    -- ── welcome (password sign-up) ───────────────────────────────────────────
    ('welcome', 'en', 'Email',
        'Welcome to ROCloud',
        'Hello {{OwnerName}}, welcome to ROCloud!

Your portal is ready at: {{LoginUrl}}
Sign in there any time with this email address. Bookmark the link so you can find it again.

Your free trial runs until {{TrialEndsAt}}.'),
    ('welcome', 'hi', 'Email',
        'ROCloud में आपका स्वागत है',
        'नमस्ते {{OwnerName}}, ROCloud में आपका स्वागत है!

आपका पोर्टल तैयार है: {{LoginUrl}}
आप कभी भी इसी ईमेल पते से साइन इन कर सकते हैं। लिंक को बुकमार्क कर लें।

आपका निःशुल्क ट्रायल {{TrialEndsAt}} तक चलेगा।'),
    ('welcome', 'gu', 'Email',
        'ROCloud માં આપનું સ્વાગત છે',
        'નમસ્તે {{OwnerName}}, ROCloud માં આપનું સ્વાગત છે!

તમારું પોર્ટલ તૈયાર છે: {{LoginUrl}}
તમે કોઈપણ સમયે આ જ ઈમેલ સરનામાંથી સાઇન ઇન કરી શકો છો. લિંક બુકમાર્ક કરી લો.

તમારું મફત ટ્રાયલ {{TrialEndsAt}} સુધી ચાલશે.'),

    -- ── welcome_google (passwordless Google sign-up) ─────────────────────────
    ('welcome_google', 'en', 'Email',
        'Welcome to ROCloud',
        'Hello {{OwnerName}}, welcome to ROCloud!

Your portal is ready at: {{LoginUrl}}
Sign in there any time with Google using this email address. Bookmark the link so you can find it again.

Your free trial runs until {{TrialEndsAt}}.'),
    ('welcome_google', 'hi', 'Email',
        'ROCloud में आपका स्वागत है',
        'नमस्ते {{OwnerName}}, ROCloud में आपका स्वागत है!

आपका पोर्टल तैयार है: {{LoginUrl}}
आप कभी भी Google से इसी ईमेल पते का उपयोग करके साइन इन कर सकते हैं। लिंक को बुकमार्क कर लें।

आपका निःशुल्क ट्रायल {{TrialEndsAt}} तक चलेगा।'),
    ('welcome_google', 'gu', 'Email',
        'ROCloud માં આપનું સ્વાગત છે',
        'નમસ્તે {{OwnerName}}, ROCloud માં આપનું સ્વાગત છે!

તમારું પોર્ટલ તૈયાર છે: {{LoginUrl}}
તમે કોઈપણ સમયે Google વડે આ જ ઈમેલ સરનામાંનો ઉપયોગ કરીને સાઇન ઇન કરી શકો છો. લિંક બુકમાર્ક કરી લો.

તમારું મફત ટ્રાયલ {{TrialEndsAt}} સુધી ચાલશે.'),

    -- ── password_reset (carries the clickable reset link) ────────────────────
    ('password_reset', 'en', 'Email',
        'Reset your ROCloud password',
        'Hi {{Name}}, we received a request to reset the password for your ROCloud account.

<a href="{{ResetUrl}}">Reset your password</a>

This link is valid for {{Minutes}} minutes and can be used once.

If you didn''t request this, you can safely ignore this email — your password will not change.'),
    ('password_reset', 'hi', 'Email',
        'अपना ROCloud पासवर्ड रीसेट करें',
        'नमस्ते {{Name}}, हमें आपके ROCloud खाते का पासवर्ड रीसेट करने का अनुरोध मिला है।

<a href="{{ResetUrl}}">पासवर्ड रीसेट करें</a>

यह लिंक {{Minutes}} मिनट के लिए मान्य है और केवल एक बार उपयोग किया जा सकता है।

यदि आपने यह अनुरोध नहीं किया है, तो इस ईमेल को अनदेखा करें — आपका पासवर्ड नहीं बदलेगा।'),
    ('password_reset', 'gu', 'Email',
        'તમારો ROCloud પાસવર્ડ રીસેટ કરો',
        'નમસ્તે {{Name}}, અમને તમારા ROCloud ખાતાનો પાસવર્ડ રીસેટ કરવાની વિનંતી મળી છે.

<a href="{{ResetUrl}}">પાસવર્ડ રીસેટ કરો</a>

આ લિંક {{Minutes}} મિનિટ માટે માન્ય છે અને એક જ વાર વાપરી શકાય છે.

જો તમે આ વિનંતી કરી ન હોય, તો આ ઈમેલ અવગણો — તમારો પાસવર્ડ બદલાશે નહીં.'),

    -- ── subscription_receipt (payment received) ──────────────────────────────
    ('subscription_receipt', 'en', 'Email',
        'Payment received — ROCloud invoice {{InvoiceNumber}}',
        'Hi {{TenantName}}, we''ve received your payment of ₹{{Amount}} for invoice {{InvoiceNumber}}. Your ROCloud subscription is active. Thank you!'),
    ('subscription_receipt', 'hi', 'Email',
        'भुगतान प्राप्त हुआ — ROCloud इनवॉइस {{InvoiceNumber}}',
        'नमस्ते {{TenantName}}, हमें इनवॉइस {{InvoiceNumber}} के लिए आपका ₹{{Amount}} का भुगतान प्राप्त हो गया है। आपकी ROCloud सदस्यता सक्रिय है। धन्यवाद!'),
    ('subscription_receipt', 'gu', 'Email',
        'ચુકવણી પ્રાપ્ત થઈ — ROCloud ઇન્વોઇસ {{InvoiceNumber}}',
        'નમસ્તે {{TenantName}}, અમને ઇન્વોઇસ {{InvoiceNumber}} માટે તમારી ₹{{Amount}} ની ચુકવણી પ્રાપ્ત થઈ છે. તમારું ROCloud સબ્સ્ક્રિપ્શન સક્રિય છે. આભાર!')
) AS v(code, lang, channel, subject, body)
WHERE NOT EXISTS (
    SELECT 1 FROM public.notification_templates t
    WHERE t.tenant_id IS NULL
      AND t.template_code = v.code
      AND t.language_code = v.lang
      AND t.channel = v.channel
);
