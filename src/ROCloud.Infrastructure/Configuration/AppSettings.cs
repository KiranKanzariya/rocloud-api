using Microsoft.Extensions.Configuration;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Infrastructure.Configuration;

/// <summary>
/// Reads operational settings from appsettings (IConfiguration) for the Application layer.
/// Every value falls back to the previous hard-coded default, so a missing key is a no-op.
/// </summary>
public sealed class AppSettings : IAppSettings
{
    private readonly IConfiguration _config;

    public AppSettings(IConfiguration config) => _config = config;

    private int Int(string key, int fallback) =>
        int.TryParse(_config[key], out var v) ? v : fallback;

    private long Long(string key, long fallback) =>
        long.TryParse(_config[key], out var v) ? v : fallback;

    private bool Bool(string key, bool fallback) =>
        bool.TryParse(_config[key], out var v) ? v : fallback;

    public string WebUrl => _config["App:WebUrl"] is { Length: > 0 } u ? u : "https://app.rocloud.app";
    public string TimeZone => _config["App:TimeZone"] is { Length: > 0 } tz ? tz : "Asia/Kolkata";
    public string TenantUrlFormat =>
        _config["App:TenantUrlFormat"] is { Length: > 0 } u ? u : "https://{subdomain}.rocloud.app";
    public int RefreshTokenExpiryDays => Int("Jwt:RefreshTokenExpiryDays", 30);
    public int TrialDays => Int("Tenant:TrialDays", 14);
    public int MaxLoginAttempts => Int("Security:MaxLoginAttempts", 5);
    public int LockoutMinutes => Int("Security:LockoutMinutes", 15);
    public int PasswordResetTokenTtlMinutes => Int("Security:PasswordResetTokenTtlMinutes", 60);
    public decimal DefaultGstRate =>
        decimal.TryParse(_config["Billing:GstRate"], out var v) ? v : 0.18m;
    public int InvoiceDueInDays => Int("Billing:DefaultDueInDays", 15);
    public int InvoiceLinkExpiryDays => Int("Billing:InvoiceLinkExpiryDays", 7);
    public long DeliveryProofMaxBytes => Long("Files:DeliveryProofMaxBytes", 5 * 1024 * 1024);
    public bool EmailEnabled => Bool("Notifications:EmailEnabled", true);
    public bool SmsEnabled => Bool("Notifications:SmsEnabled", true);
    public bool WhatsAppEnabled => Bool("Notifications:WhatsAppEnabled", true);
    public bool InvoiceSentEnabled => Bool("Notifications:CustomerNotifications:InvoiceSent", true);
    public bool PaymentReminderEnabled => Bool("Notifications:CustomerNotifications:PaymentReminder", true);
    public bool AmcReminderEnabled => Bool("Notifications:CustomerNotifications:AmcReminder", true);
    public bool AdvanceOrderReminderEnabled => Bool("Notifications:CustomerNotifications:AdvanceOrderReminder", true);
    public int SubscriptionInvoiceLeadDays => Int("Jobs:SubscriptionInvoiceLeadDays", 5);
    public int SubscriptionOverdueGraceDays => Int("Subscription:OverdueGraceDays", 7);
}
