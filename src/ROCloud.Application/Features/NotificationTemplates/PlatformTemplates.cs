namespace ROCloud.Application.Features.NotificationTemplates;

/// <summary>
/// The templates ROCloud sends to the TENANT (its owner/users) rather than the tenant sends to ITS
/// customers. They are rendered with a null tenant id — see the RenderAsync call sites — so a tenant
/// override would never be read even if one existed.
///
/// That makes them meaningless in the owner portal: the owner cannot change the wording of their own
/// password-reset mail, and a row saved against one would be dead weight. They are therefore hidden
/// from the list AND rejected on write. Their wording is edited in the ADMIN portal, which owns the
/// system defaults.
///
/// The test is simply "who is the recipient": a customer-facing template (invoice_sent,
/// payment_reminder, amc_reminder, advance_order_reminder) renders with the tenant's id and IS
/// overridable. Add a code here whenever a new platform→tenant mail is introduced.
/// </summary>
public static class PlatformTemplates
{
    public static readonly string[] Codes =
    [
        "welcome",               // Register            → the new owner
        "welcome_google",        // RegisterGoogle      → the new owner
        "password_reset",        // ForgotPassword      → any tenant user
        "subscription_expiry",   // SubscriptionExpiryJob      → the owner
        "subscription_invoice",  // SubscriptionInvoiceDelivery → the owner
        "subscription_receipt",  // SubscriptionInvoiceDelivery → the owner
    ];

    public static bool IsPlatformOnly(string? templateCode) =>
        templateCode is not null && Codes.Contains(templateCode);
}
