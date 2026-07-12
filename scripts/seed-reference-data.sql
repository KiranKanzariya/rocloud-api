-- ============================================================================
-- ROCloud — SEED / REFERENCE DATA
-- ============================================================================
-- The shared, non-tenant "reference" data every ROCloud database needs before it
-- can serve traffic: plans, the permission catalog, the global audit-settings row,
-- and the system-default notification templates (en/hi/gu).
--
-- This is DATA only — it assumes the schema already exists (created from
-- scripts/master.sql). It does NOT create tables, roles, or the hangfire schema.
--
-- HOW TO RUN — as the postgres superuser (the seeded tables are postgres-owned):
--
--     psql -U postgres -d rocloud_prod -f scripts/seed-reference-data.sql
--
-- Fully IDEMPOTENT: every statement guards itself (ON CONFLICT DO NOTHING /
-- NOT EXISTS), so re-running only fills what is missing and never duplicates.
-- Safe on a brand-new database or an already-populated one.
--
-- NOT seeded here:
--   • The platform SuperAdmin login — created by scripts/create-platform-admin.sql
--     (run as postgres; edit its email/password first).
--   • recurring_job_settings — the API inserts those default rows itself on startup
--     (RecurringJobSettingsStore.SeedIfMissing, ON CONFLICT DO NOTHING) once it
--     registers its Hangfire jobs.
-- ============================================================================


-- ────────────────────────────────────────────────────────────────────────────
-- 1. PLANS  (Basic / Pro / Enterprise)
-- ────────────────────────────────────────────────────────────────────────────
INSERT INTO public.plans VALUES ('60584fa1-b0e9-4583-ba41-dc0075c03241', 'Pro', 'Pro', 2499.00, 24990.00, 1000, 10, 5, false, false, false, false, true, '2026-06-19 10:44:41.287753+05:30', NULL) ON CONFLICT DO NOTHING;
INSERT INTO public.plans VALUES ('d7a4d8f4-65e5-4be6-855e-359baa3e12da', 'Basic', 'Basic', 1099.00, 9990.00, 200, 3, 1, false, false, false, false, true, '2026-06-19 10:44:41.287753+05:30', '2026-06-20 16:18:06.468513+05:30') ON CONFLICT DO NOTHING;
INSERT INTO public.plans VALUES ('8d73999a-8958-4c2f-b2d9-793b6cec0797', 'Enterprise', 'Enterprise', 5999.00, 59990.00, 0, 0, 0, false, true, false, false, true, '2026-06-19 10:44:41.287753+05:30', NULL) ON CONFLICT DO NOTHING;


-- ────────────────────────────────────────────────────────────────────────────
-- 2. PERMISSIONS  (the full 33-permission catalog)
-- ────────────────────────────────────────────────────────────────────────────
INSERT INTO public.permissions VALUES ('52292ffc-c2e5-44cc-a31a-73c060f542b2', 'Customers', 'View', 'Customers.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('b52ebb02-027e-434a-b1f5-a4ce85d73d9b', 'Customers', 'Create', 'Customers.Create') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('6ad78d35-6c31-4d4e-a386-0e8b5337f003', 'Customers', 'Edit', 'Customers.Edit') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('451a0c6d-2f5d-4071-a42c-5ac3465a3cb9', 'Customers', 'Delete', 'Customers.Delete') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('16479728-ac27-4047-874b-5da0a709c254', 'Orders', 'View', 'Orders.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('5593d363-ffef-4db8-8782-d8a48315eb42', 'Orders', 'Create', 'Orders.Create') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('7caba015-5615-4b5d-a0cb-3e98fa18d22b', 'Orders', 'Edit', 'Orders.Edit') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('13482731-e745-4700-b572-f9d25b03d622', 'Orders', 'Cancel', 'Orders.Cancel') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('8b03097d-3093-4da1-92ed-b5b6f36956db', 'Deliveries', 'View', 'Deliveries.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('3458a52d-3cf6-43ae-87d0-c30b38e85d18', 'Deliveries', 'Update', 'Deliveries.Update') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('7291da5b-d9fc-4be3-8a0a-eb28f2fd7af9', 'Deliveries', 'ViewOwn', 'Deliveries.ViewOwn') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('03e3f16b-40ce-4d71-8ed7-9b2f10ad9ddf', 'Inventory', 'View', 'Inventory.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('dd72ee3c-9723-40ce-b3e9-337ec17dd9b0', 'Inventory', 'Manage', 'Inventory.Manage') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('1d0b24db-2568-4073-a2a3-e5040a533211', 'Invoices', 'View', 'Invoices.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('1b4cfd37-5278-4a82-bc95-09cfff3126b5', 'Invoices', 'Create', 'Invoices.Create') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('dbfa5cc0-5a67-4fc4-b1f3-28f68edea181', 'Invoices', 'Edit', 'Invoices.Edit') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('2fa495bf-0c50-4951-900e-4c0c335e45db', 'Payments', 'View', 'Payments.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('236948ee-360f-4171-9e33-a82eeb57b82a', 'Payments', 'Collect', 'Payments.Collect') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('b2540158-2ec7-40ec-a78f-c0408b1fc086', 'Payments', 'Manage', 'Payments.Manage') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('5688ccab-db22-4e7b-9e1c-a7908a3a2159', 'Reports', 'View', 'Reports.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('856e41f0-f5c1-4c5f-8733-e88abf36ff49', 'AMC', 'View', 'AMC.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('f6aad34e-8ec6-490b-b77e-0a90db2330a2', 'AMC', 'Manage', 'AMC.Manage') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('040e6386-d251-4786-b9a9-5a71e864688e', 'AMC', 'Update', 'AMC.Update') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('6a3ae0c6-b0f0-4d95-b3a7-acd99013a5d0', 'Users', 'View', 'Users.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('a9185973-bcbb-4762-98b9-c05354c03f9d', 'Users', 'Manage', 'Users.Manage') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('2c1f7f4e-3a5d-4b8e-9c17-6d0a1b2e4c93', 'Roles', 'View', 'Roles.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('af1d36f9-a405-4572-8f91-b86ae190446b', 'Roles', 'Manage', 'Roles.Manage') ON CONFLICT DO NOTHING;
-- Settings is not one permission but one per PAGE (see migration-AddPageWiseSettingsPermissions.sql).
-- The tenant's own ROCloud subscription has no permission at all: it is [RequireOwner], so that no
-- custom role can ever be granted the right to change the plan or spend the owner's money.
INSERT INTO public.permissions VALUES ('3f2b9c14-6d81-4a27-9e53-1c8f0b6a2d47', 'Areas', 'View', 'Areas.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('8a15e7d2-0c64-4b39-a7f8-52d9e3b06c1a', 'Areas', 'Manage', 'Areas.Manage') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('c94d63f8-27ba-4e15-8306-4f7a1e9d5b28', 'Notifications', 'View', 'Notifications.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('5e07a1b6-9f3d-4c82-b1e4-7036c8d4a9f5', 'Notifications', 'Manage', 'Notifications.Manage') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('b6c8f430-1e59-42d7-9a06-3d8b5f2c7e41', 'Business Profile', 'View', 'BusinessProfile.View') ON CONFLICT DO NOTHING;
INSERT INTO public.permissions VALUES ('2d94a7e5-8b36-4f01-93c7-6a1e0d5b8c93', 'Business Profile', 'Manage', 'BusinessProfile.Manage') ON CONFLICT DO NOTHING;


-- ────────────────────────────────────────────────────────────────────────────
-- 3. AUDIT SETTINGS  (single global row, defaults)
-- ────────────────────────────────────────────────────────────────────────────
INSERT INTO public.audit_settings (id)
SELECT public.uuid_generate_v4()
WHERE NOT EXISTS (SELECT 1 FROM public.audit_settings);


-- ────────────────────────────────────────────────────────────────────────────
-- 4. NOTIFICATION TEMPLATES  (system defaults, tenant_id = NULL, en/hi/gu)
--    39 rows total: 13 template/channel combos × 3 languages.
--    Guarded with NOT EXISTS (the UNIQUE constraint treats NULL tenant_id as
--    distinct, so ON CONFLICT would not fire). A tenant may later override any row.
-- ────────────────────────────────────────────────────────────────────────────

-- 4a. invoice_sent (Email), payment_reminder + amc_reminder (WhatsApp),
--     subscription_expiry (Email)
INSERT INTO public.notification_templates
    (id, tenant_id, template_code, language_code, channel, subject, body, created_at, updated_at)
SELECT gen_random_uuid(), NULL, v.code, v.lang, v.channel, v.subject, v.body, now(), now()
FROM (VALUES
    ('invoice_sent', 'en', 'Email',
        'Invoice {{InvoiceNumber}}',
        'Hi {{CustomerName}}, your invoice {{InvoiceNumber}} is attached to this email. Thank you.'),
    ('invoice_sent', 'hi', 'Email',
        'इनवॉइस {{InvoiceNumber}}',
        'नमस्ते {{CustomerName}}, आपका इनवॉइस {{InvoiceNumber}} इस ईमेल के साथ संलग्न है। धन्यवाद।'),
    ('invoice_sent', 'gu', 'Email',
        'ઇન્વોઇસ {{InvoiceNumber}}',
        'નમસ્તે {{CustomerName}}, તમારું ઇન્વોઇસ {{InvoiceNumber}} આ ઈમેલ સાથે જોડેલું છે. આભાર.'),

    ('payment_reminder', 'en', 'WhatsApp', NULL,
        'Hi {{CustomerName}}, you have ₹{{Amount}} outstanding ({{DaysOverdue}} days overdue). Please clear your dues. Thank you.'),
    ('payment_reminder', 'hi', 'WhatsApp', NULL,
        'नमस्ते {{CustomerName}}, आपके ₹{{Amount}} बकाया हैं ({{DaysOverdue}} दिन से)। कृपया अपना भुगतान करें। धन्यवाद।'),
    ('payment_reminder', 'gu', 'WhatsApp', NULL,
        'નમસ્તે {{CustomerName}}, તમારા ₹{{Amount}} બાકી છે ({{DaysOverdue}} દિવસથી). કૃપા કરીને ચુકવણી કરો. આભાર.'),

    ('amc_reminder', 'en', 'WhatsApp', NULL,
        'Hi {{CustomerName}}, your service visit ({{TicketNumber}}) is scheduled for {{ScheduledDate}}. Our technician will reach you. Thank you.'),
    ('amc_reminder', 'hi', 'WhatsApp', NULL,
        'नमस्ते {{CustomerName}}, आपकी सर्विस विज़िट ({{TicketNumber}}) {{ScheduledDate}} को निर्धारित है। हमारा तकनीशियन आपके पास पहुंचेगा। धन्यवाद।'),
    ('amc_reminder', 'gu', 'WhatsApp', NULL,
        'નમસ્તે {{CustomerName}}, તમારી સર્વિસ મુલાકાત ({{TicketNumber}}) {{ScheduledDate}} ના રોજ નિર્ધારિત છે. અમારો ટેકનિશિયન તમારી પાસે પહોંચશે. આભાર.'),

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
    WHERE t.tenant_id IS NULL AND t.template_code = v.code
      AND t.language_code = v.lang AND t.channel = v.channel
);

-- 4b. Email fallback bodies for the reminder events
--     payment_reminder / amc_reminder / advance_order_reminder (Email)
INSERT INTO public.notification_templates
    (id, tenant_id, template_code, language_code, channel, subject, body, created_at, updated_at)
SELECT gen_random_uuid(), NULL, v.code, v.lang, v.channel, v.subject, v.body, now(), now()
FROM (VALUES
    ('payment_reminder', 'en', 'Email',
        'Payment reminder',
        'Hi {{CustomerName}}, you have ₹{{Amount}} outstanding ({{DaysOverdue}} days overdue). Please clear your dues. Thank you.'),
    ('payment_reminder', 'hi', 'Email',
        'भुगतान अनुस्मारक',
        'नमस्ते {{CustomerName}}, आपके ₹{{Amount}} बकाया हैं ({{DaysOverdue}} दिन से)। कृपया अपना भुगतान करें। धन्यवाद।'),
    ('payment_reminder', 'gu', 'Email',
        'ચુકવણી રિમાઇન્ડર',
        'નમસ્તે {{CustomerName}}, તમારા ₹{{Amount}} બાકી છે ({{DaysOverdue}} દિવસથી). કૃપા કરીને ચુકવણી કરો. આભાર.'),

    ('amc_reminder', 'en', 'Email',
        'Upcoming service visit reminder',
        'Hi {{CustomerName}}, your service visit ({{TicketNumber}}) is scheduled for {{ScheduledDate}}. Our technician will reach you. Thank you.'),
    ('amc_reminder', 'hi', 'Email',
        'आगामी सर्विस विज़िट अनुस्मारक',
        'नमस्ते {{CustomerName}}, आपकी सर्विस विज़िट ({{TicketNumber}}) {{ScheduledDate}} को निर्धारित है। हमारा तकनीशियन आपके पास पहुंचेगा। धन्यवाद।'),
    ('amc_reminder', 'gu', 'Email',
        'આગામી સર્વિસ મુલાકાત રિમાઇન્ડર',
        'નમસ્તે {{CustomerName}}, તમારી સર્વિસ મુલાકાત ({{TicketNumber}}) {{ScheduledDate}} ના રોજ નિર્ધારિત છે. અમારો ટેકનિશિયન તમારી પાસે પહોંચશે. આભાર.'),

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
    WHERE t.tenant_id IS NULL AND t.template_code = v.code
      AND t.language_code = v.lang AND t.channel = v.channel
);

-- 4c. advance_order_reminder (WhatsApp)
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
    WHERE t.tenant_id IS NULL AND t.template_code = v.code
      AND t.language_code = v.lang AND t.channel = v.channel
);

-- 4d. subscription_invoice (Email) — platform → tenant "please pay" mail for a Pending
--     ROCloud subscription invoice (guide §25/§26). ROCloud-branded, like subscription_expiry.
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
    WHERE t.tenant_id IS NULL AND t.template_code = v.code
      AND t.language_code = v.lang AND t.channel = v.channel
);


-- 4e. Remaining platform → tenant-owner mail: welcome / welcome_google / password_reset /
--     subscription_receipt. Each handler keeps its English text as a built-in fallback.
--     password_reset carries an <a href="{{ResetUrl}}"> link — the reset page reads ?token=.
INSERT INTO public.notification_templates
    (id, tenant_id, template_code, language_code, channel, subject, body, created_at, updated_at)
SELECT gen_random_uuid(), NULL, v.code, v.lang, v.channel, v.subject, v.body, now(), now()
FROM (VALUES
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
    WHERE t.tenant_id IS NULL AND t.template_code = v.code
      AND t.language_code = v.lang AND t.channel = v.channel
);


-- ────────────────────────────────────────────────────────────────────────────
-- 5. VERIFY  (expected counts after running)
-- ────────────────────────────────────────────────────────────────────────────
-- NOTE: the platform SuperAdmin login is created separately by
-- scripts/create-platform-admin.sql (run as postgres) — it is not seeded here.
SELECT 'plans'                  AS table, count(*) AS rows, 3  AS expected FROM public.plans
UNION ALL SELECT 'permissions',            count(*), 28 FROM public.permissions
UNION ALL SELECT 'audit_settings',         count(*), 1  FROM public.audit_settings
UNION ALL SELECT 'notification_templates (system default)', count(*), 39
          FROM public.notification_templates WHERE tenant_id IS NULL
ORDER BY 1;
