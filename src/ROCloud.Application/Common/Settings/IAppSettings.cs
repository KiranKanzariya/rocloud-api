namespace ROCloud.Application.Common.Settings;

/// <summary>
/// Strongly-typed access to operational settings for the Application layer, which cannot read
/// IConfiguration directly (Clean Architecture). Implemented in Infrastructure from appsettings,
/// with sensible fallbacks so a missing key never changes behaviour.
/// </summary>
public interface IAppSettings
{
    /// <summary>Public web app base URL — used to build links in emails (App:WebUrl).</summary>
    string WebUrl { get; }

    /// <summary>
    /// IANA timezone name used to pin the PostgreSQL session so in-SQL calendar-date derivation
    /// (EXTRACT / date_trunc / <c>::date</c> on timestamptz columns) is computed in this zone,
    /// independent of the host machine (App:TimeZone). Default <c>Asia/Kolkata</c> (IST).
    /// </summary>
    string TimeZone { get; }

    /// <summary>
    /// Tenant portal URL template with a <c>{subdomain}</c> placeholder, used to tell a new owner
    /// where to sign in (App:TenantUrlFormat). Default <c>https://{subdomain}.rocloud.app</c>.
    /// </summary>
    string TenantUrlFormat { get; }

    /// <summary>Refresh-token lifetime in days (Jwt:RefreshTokenExpiryDays). Default 30.</summary>
    int RefreshTokenExpiryDays { get; }

    /// <summary>Free-trial length for new tenants in days (Tenant:TrialDays). Default 14.</summary>
    int TrialDays { get; }

    /// <summary>Failed logins before lockout (Security:MaxLoginAttempts). Default 5.</summary>
    int MaxLoginAttempts { get; }

    /// <summary>Login lockout duration in minutes (Security:LockoutMinutes). Default 15.</summary>
    int LockoutMinutes { get; }

    /// <summary>Password-reset token lifetime in minutes (Security:PasswordResetTokenTtlMinutes). Default 60.</summary>
    int PasswordResetTokenTtlMinutes { get; }

    /// <summary>Default GST rate when none is supplied (Billing:GstRate). Default 0.18.</summary>
    decimal DefaultGstRate { get; }

    /// <summary>Default invoice due window in days (Billing:DefaultDueInDays). Default 15.</summary>
    int InvoiceDueInDays { get; }

    /// <summary>How long an emailed invoice download link stays valid, in days (Billing:InvoiceLinkExpiryDays). Default 7.</summary>
    int InvoiceLinkExpiryDays { get; }

    /// <summary>Max delivery-proof upload size in bytes (Files:DeliveryProofMaxBytes). Default 5 MB.</summary>
    long DeliveryProofMaxBytes { get; }

    /// <summary>Master switch for all outbound email (Notifications:EmailEnabled). Default true.</summary>
    bool EmailEnabled { get; }

    /// <summary>Master switch for all outbound SMS (Notifications:SmsEnabled). Default true.</summary>
    bool SmsEnabled { get; }

    /// <summary>
    /// Master switch for all outbound WhatsApp (Notifications:WhatsAppEnabled). Default true.
    /// This is checked before the per-tenant plan feature: WhatsApp goes out only when this is on
    /// AND the tenant's plan includes WhatsApp.
    /// </summary>
    bool WhatsAppEnabled { get; }

    // ── Per-event switches for the tenant → customer notifications (Notifications:CustomerNotifications).
    //    These sit on top of the channel master switches above: an event goes out only when its own
    //    switch is on AND the delivering channel is enabled. Turned off for v1. Missing key = true.

    /// <summary>Whether emailing the invoice to the customer is enabled on send
    /// (Notifications:CustomerNotifications:InvoiceSent). Default true.</summary>
    bool InvoiceSentEnabled { get; }

    /// <summary>Whether the overdue payment reminder job sends to customers
    /// (Notifications:CustomerNotifications:PaymentReminder). Default true.</summary>
    bool PaymentReminderEnabled { get; }

    /// <summary>Whether the upcoming AMC/service-visit reminder job sends to customers
    /// (Notifications:CustomerNotifications:AmcReminder). Default true.</summary>
    bool AmcReminderEnabled { get; }

    /// <summary>Whether the day-before advance-order reminder job sends to customers
    /// (Notifications:CustomerNotifications:AdvanceOrderReminder). Default true.</summary>
    bool AdvanceOrderReminderEnabled { get; }

    /// <summary>How many days before a paid subscription's end date a renewal invoice may be raised —
    /// used by the expiry job and the on-demand "Renew now" eligibility check
    /// (Jobs:SubscriptionInvoiceLeadDays). Default 5.</summary>
    int SubscriptionInvoiceLeadDays { get; }
}
