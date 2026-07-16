using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Infrastructure.Caching;
using ROCloud.Infrastructure.ExternalServices;
using ROCloud.Infrastructure.Identity;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ROCloud.Infrastructure.Persistence.Interceptors;
using ROCloud.Infrastructure.Storage;
using ROCloud.Infrastructure.Time;

namespace ROCloud.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured. In Production, supply it via the " +
                "ConnectionStrings__Default environment variable.");

        services.AddScoped<TenantConnectionInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseNpgsql(connectionString)
                   .AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<ITenantContext, TenantContext>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<Application.Common.Settings.IAppSettings, Configuration.AppSettings>();

        // Identity / auth (guide §5, §10.2, §10.3)
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IGoogleAuthService, GoogleAuthService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // External services (guide §14). Email + MSG91 SMS/WhatsApp. All degrade to
        // logging when their API keys aren't configured, so dev/tests run without credentials.
        // Email provider is config-selected ("Email:Provider" = SendGrid | Resend); default SendGrid.
        // The concrete provider is registered under its own type, then wrapped by BrandedEmailService
        // so every outgoing email gets the shared HTML shell + plain-text alternative.
        var useResend = string.Equals(configuration["Email:Provider"], "Resend", StringComparison.OrdinalIgnoreCase);
        if (useResend)
            services.AddHttpClient<ResendEmailService>();
        else
            services.AddScoped<SendGridEmailService>();
        services.AddScoped<IEmailBrandContext, EmailBrandContext>();
        services.AddScoped<IEmailService>(sp => new BrandedEmailService(
            useResend
                ? sp.GetRequiredService<ResendEmailService>()
                : sp.GetRequiredService<SendGridEmailService>(),
            sp.GetRequiredService<IEmailBrandContext>(),
            sp.GetRequiredService<Application.Common.Settings.IAppSettings>(),
            sp.GetRequiredService<ILogger<BrandedEmailService>>()));
        services.AddHttpClient<ISmsService, Msg91SmsService>();
        services.AddHttpClient<IWhatsAppService, Msg91WhatsAppService>();

        // Background jobs (guide §14) — registered for DI resolution by Hangfire.
        services.AddScoped<BackgroundJobs.TenantJobRunner>();
        services.AddScoped<BackgroundJobs.MonthlyBillingJob>();
        services.AddScoped<BackgroundJobs.DailyDeliveryRolloverJob>();
        services.AddScoped<BackgroundJobs.SubscriptionExpiryJob>();
        services.AddScoped<BackgroundJobs.PaymentReminderJob>();
        services.AddScoped<BackgroundJobs.InvoiceAllocationSyncJob>();
        services.AddScoped<BackgroundJobs.AmcReminderJob>();
        services.AddScoped<BackgroundJobs.AdvanceOrderReminderJob>();
        services.AddScoped<BackgroundJobs.AuditLogPartitionJob>();
        services.AddScoped<BackgroundJobs.AuditLogRetentionJob>();
        services.AddScoped<BackgroundJobs.LogRetentionJob>();
        services.AddScoped<BackgroundJobs.PaymentReconciliationJob>();

        // Read/control side of Hangfire for the super-admin portal's Background Jobs page (guide §26).
        services.AddSingleton<BackgroundJobs.RecurringJobSettingsStore>();
        services.AddScoped<IBackgroundJobService, BackgroundJobs.HangfireJobService>();

        // Razorpay (guide §10) — raw HttpClient, no SDK. PCI scope stays with Razorpay (§10.18).
        services.AddHttpClient<IRazorpayService, RazorpayService>();

        // Invoice PDF generation (guide §10) via QuestPDF.
        services.AddSingleton<IInvoicePdfGenerator, ROCloud.Infrastructure.Pdf.InvoicePdfGenerator>();
        services.AddSingleton<ISubscriptionInvoicePdfGenerator, ROCloud.Infrastructure.Pdf.SubscriptionInvoicePdfGenerator>();

        // Subscription-invoice PDF + owner email delivery (guide §25/§26).
        services.AddScoped<
            ISubscriptionInvoiceDelivery,
            Application.Features.Subscription.Services.SubscriptionInvoiceDelivery>();

        // Reporting — raw ADO.NET (guide §9b/§12) + CSV/XLSX export.
        services.AddScoped<IReportRepository, ROCloud.Infrastructure.Reports.ReportRepository>();
        services.AddSingleton<IReportExporter, ROCloud.Infrastructure.Reports.ReportExporter>();

        // Audit logging (guide §10.14) — raw INSERT into the append-only audit_logs table.
        services.AddSingleton<IAuditLogWriter, ROCloud.Infrastructure.Persistence.AuditLogWriter>();
        // Cached global audit configuration (read by AuditMiddleware on every request).
        services.AddSingleton<IAuditSettingsProvider, ROCloud.Infrastructure.Persistence.AuditSettingsProvider>();

        // Default (unbounded) IMemoryCache shared by the framework / AspNetCoreRateLimit.
        // It must NOT have a SizeLimit: AspNetCoreRateLimit writes cache entries without a
        // Size, which throws when a SizeLimit is configured.
        services.AddMemoryCache();

        // Caching — in-memory (v1), behind ICacheService (guide §4b). Swap to Redis later.
        // Given its OWN bounded MemoryCache (Size=1 per entry) so the shared cache above
        // stays Size-less for rate limiting. Limits + default expiry come from Cache:* config.
        var cacheSizeLimit = long.TryParse(configuration["Cache:SizeLimit"], out var sl) ? sl : 10_000;
        var cacheCompaction = double.TryParse(configuration["Cache:CompactionPercentage"], out var cp) ? cp : 0.2;
        var cacheExpiryMinutes = int.TryParse(configuration["Cache:DefaultExpiryMinutes"], out var em) ? em : 30;
        services.AddSingleton<ICacheService>(_ => new InMemoryCacheService(
            new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = cacheSizeLimit,
                CompactionPercentage = cacheCompaction
            }),
            TimeSpan.FromMinutes(cacheExpiryMinutes)));

        // File storage — local disk (v1), behind IFileStorage (guide §4b). Swap to S3/Supabase later.
        // Used for delivery-proof photos only; invoice PDFs are never stored (rendered on demand).
        services.AddHttpContextAccessor();

        // Persist the Data Protection key ring to a STABLE location. The signed invoice- and
        // file-download links (which stand in for authentication for logged-out customers) are protected
        // with these keys; under the default provider hosted in-process on IIS the keys can be lost on an
        // app-pool recycle/redeploy, silently invalidating every outstanding link. Path comes from
        // DataProtection:KeysPath, defaulting to a per-machine folder OUTSIDE the deploy directory so it
        // survives redeploys; SetApplicationName keeps the purpose string stable across rehosting.
        var keysPath = configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath))
            keysPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ROCloud", "DataProtection-Keys");
        services.AddDataProtection()
            .SetApplicationName("ROCloud")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.AddScoped<IFileStorage, LocalFileStorage>();

        // Signs the expiring invoice-download links emailed to customers (they have no login).
        services.AddScoped<IInvoiceLinkSigner, Security.InvoiceLinkSigner>();

        // Delivery proof photos — validation + re-encode pipeline (guide §10.11).
        services.AddScoped<
            ROCloud.Application.Features.Deliveries.Services.IDeliveryProofService,
            ROCloud.Application.Features.Deliveries.Services.DeliveryProofService>();

        // Renders outbound messages from the notification_templates table (guide §14/§24).
        services.AddScoped<
            INotificationTemplateRenderer,
            Application.Common.Services.NotificationTemplateRenderer>();

        return services;
    }
}
