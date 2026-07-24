using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROCloud.Application;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Invoices.Commands.BulkGenerateInvoices;
using ROCloud.Application.Features.Invoices.Dtos;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.BackgroundJobs;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.BackgroundJobs;

public class BackgroundJobTests
{
    private sealed class NullEmail : IEmailService
    {
        public Task<bool> SendAsync(string to, string s, string b, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class NullWhatsApp : IWhatsAppService
    {
        public Task<bool> SendAsync(string mobile, string m, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Records every WhatsApp send so a test can assert who was reminded. Registered as a
    /// singleton so the same instance is visible across the per-tenant scopes the runner creates.</summary>
    private sealed class RecordingWhatsApp : IWhatsAppService
    {
        public readonly List<(string Mobile, string Message)> Sent = new();
        public Task<bool> SendAsync(string mobile, string m, CancellationToken ct = default)
        {
            lock (Sent) Sent.Add((mobile, m));
            return Task.FromResult(true);
        }
    }

    /// <summary>Records every email send so a test can assert the WhatsApp→email fallback fired.
    /// Singleton for the same cross-scope reason as <see cref="RecordingWhatsApp"/>.</summary>
    private sealed class RecordingEmail : IEmailService
    {
        public readonly List<(string To, string Subject, string Body)> Sent = new();
        public Task<bool> SendAsync(string to, string s, string b, CancellationToken ct = default)
        {
            lock (Sent) Sent.Add((to, s, b));
            return Task.FromResult(true);
        }
    }

    private sealed class FakePdf : IInvoicePdfGenerator
    {
        public byte[] Generate(InvoicePdfModel model) => [1, 2, 3];
    }

    private sealed class FakeSubPdf : ISubscriptionInvoicePdfGenerator
    {
        public byte[] Generate(ROCloud.Application.Features.Subscription.Dtos.SubscriptionInvoicePdfModel model) => [1, 2, 3];
    }

    private sealed class FakeStorage : IFileStorage
    {
        public Task<string> UploadAsync(Stream c, string ct, Guid t, string f, string n, CancellationToken token = default)
            => Task.FromResult($"{t}/{f}/{n}");
        public Task<Stream> DownloadAsync(string p, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());
        public Task<bool> ExistsAsync(string p, CancellationToken ct = default) => Task.FromResult(false);
        public Task DeleteAsync(string p, CancellationToken ct = default) => Task.CompletedTask;
        public string GetDownloadUrl(string p, TimeSpan e) => $"http://test/{p}";
    }

    /// <param name="email">Pass a recorder to assert on what the job actually sent.</param>
    private static ServiceProvider BuildProvider(RecordingEmail? email = null)
    {
        var dbName = $"jobs-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddSingleton<ROCloud.Application.Common.Settings.IAppSettings, Auth.FakeAppSettings>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(BulkGenerateInvoicesCommand).Assembly));
        if (email is not null) services.AddSingleton<IEmailService>(email);
        else services.AddScoped<IEmailService, NullEmail>();
        services.AddScoped<IWhatsAppService, NullWhatsApp>();
        services.AddScoped<INotificationTemplateRenderer, ROCloud.Application.Common.Services.NotificationTemplateRenderer>();
        services.AddScoped<IInvoicePdfGenerator, FakePdf>();
        services.AddSingleton<ISubscriptionInvoicePdfGenerator, FakeSubPdf>();
        services.AddScoped<IFileStorage, FakeStorage>();
        services.AddScoped<ISubscriptionInvoiceDelivery, ROCloud.Application.Features.Subscription.Services.SubscriptionInvoiceDelivery>();
        services.AddScoped<TenantJobRunner>();
        services.AddScoped<MonthlyBillingJob>();
        services.AddScoped<SubscriptionExpiryJob>();
        return services.BuildServiceProvider();
    }

    /// <summary>Like <see cref="BuildProvider"/> but with a shared recording WhatsApp + email + the reminder job.
    /// Pass <paramref name="settings"/> to exercise the Notifications:* channel master switches.</summary>
    private static ServiceProvider BuildReminderProvider(
        RecordingWhatsApp whatsapp, RecordingEmail? email = null, ROCloud.Application.Common.Settings.IAppSettings? settings = null)
    {
        var dbName = $"jobs-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddSingleton<ROCloud.Application.Common.Settings.IAppSettings>(settings ?? new Auth.FakeAppSettings());
        services.AddSingleton<IWhatsAppService>(whatsapp);
        services.AddSingleton<IEmailService>(email ?? new RecordingEmail());
        services.AddScoped<IEmailBrandContext, ROCloud.Infrastructure.ExternalServices.EmailBrandContext>();
        services.AddScoped<INotificationTemplateRenderer, ROCloud.Application.Common.Services.NotificationTemplateRenderer>();
        services.AddScoped<TenantJobRunner>();
        services.AddScoped<AdvanceOrderReminderJob>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AdvanceOrderReminderJob_RemindsOnlyTomorrowsOpenAdvanceOrders()
    {
        var whatsapp = new RecordingWhatsApp();
        var provider = BuildReminderProvider(whatsapp);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, WhatsappEnabled = true });
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, PlanId = db.Plans.Local.First().Id, Name = "Co", Subdomain = "co",
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9", Status = TenantStatus.Active
            });
            db.Products.Add(new Product
            {
                Id = productId, TenantId = tenantId, Name = "20L", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
            });

            var reminded = Guid.NewGuid();
            var noMobile = Guid.NewGuid();
            db.Customers.Add(new Customer { Id = reminded, TenantId = tenantId, Name = "Event Cust", Mobile = "99999" });
            db.Customers.Add(new Customer { Id = noMobile, TenantId = tenantId, Name = "No Phone", Mobile = "" });

            // (a) tomorrow + Advance + open → REMINDED
            AddOrder(db, tenantId, reminded, productId, tomorrow, OrderType.Advance, OrderStatus.Confirmed, 200);
            // (b) tomorrow + Advance but customer has no mobile → skipped
            AddOrder(db, tenantId, noMobile, productId, tomorrow, OrderType.Advance, OrderStatus.Confirmed, 5);
            // (c) tomorrow but Regular (not an advance booking) → skipped
            AddOrder(db, tenantId, reminded, productId, tomorrow, OrderType.Regular, OrderStatus.Confirmed, 3);
            // (d) Advance but 5 days out (past the 1-day lead) → skipped
            AddOrder(db, tenantId, reminded, productId, tomorrow.AddDays(4), OrderType.Advance, OrderStatus.Confirmed, 9);
            // (e) tomorrow + Advance but cancelled → skipped
            AddOrder(db, tenantId, reminded, productId, tomorrow, OrderType.Advance, OrderStatus.Cancelled, 9);
            await db.SaveChangesAsync();
        }

        await provider.GetRequiredService<AdvanceOrderReminderJob>().ExecuteAsync(CancellationToken.None);

        Assert.Single(whatsapp.Sent);
        Assert.Equal("99999", whatsapp.Sent[0].Mobile);
        Assert.Contains("200", whatsapp.Sent[0].Message);   // quantity token rendered
    }

    [Fact]
    public async Task AdvanceOrderReminderJob_FallsBackToEmailWhenWhatsappDisabled()
    {
        var whatsapp = new RecordingWhatsApp();
        var email = new RecordingEmail();
        var provider = BuildReminderProvider(whatsapp, email);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Plan WITHOUT WhatsApp — the reminder must fall back to email for customers who have one.
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Basic", PlanType = PlanType.Basic, WhatsappEnabled = false });
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, PlanId = db.Plans.Local.First().Id, Name = "Aqua Co", Subdomain = "co",
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9", Status = TenantStatus.Active
            });
            db.Products.Add(new Product
            {
                Id = productId, TenantId = tenantId, Name = "20L", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
            });
            var emailCust = Guid.NewGuid();
            var noContact = Guid.NewGuid();
            // Has an email → gets the fallback mail even though the plan has no WhatsApp.
            db.Customers.Add(new Customer { Id = emailCust, TenantId = tenantId, Name = "Event Cust", Mobile = "99999", Email = "cust@x.com" });
            // Mobile only, no email → nothing to fall back to, so skipped.
            db.Customers.Add(new Customer { Id = noContact, TenantId = tenantId, Name = "No Email", Mobile = "88888" });
            AddOrder(db, tenantId, emailCust, productId, tomorrow, OrderType.Advance, OrderStatus.Confirmed, 200);
            AddOrder(db, tenantId, noContact, productId, tomorrow, OrderType.Advance, OrderStatus.Confirmed, 5);
            await db.SaveChangesAsync();
        }

        await provider.GetRequiredService<AdvanceOrderReminderJob>().ExecuteAsync(CancellationToken.None);

        Assert.Empty(whatsapp.Sent);              // plan has no WhatsApp → never uses that channel
        Assert.Single(email.Sent);                // only the customer with an email is reached
        Assert.Equal("cust@x.com", email.Sent[0].To);
        Assert.Contains("200", email.Sent[0].Body);   // quantity token rendered into the fallback body
    }

    [Fact]
    public async Task AdvanceOrderReminderJob_MasterSwitchOffOverridesPlan_FallsBackToEmail()
    {
        // Config master switch is checked BEFORE the plan feature: even though the plan HAS WhatsApp,
        // Notifications:WhatsAppEnabled = false routes the reminder to email.
        var whatsapp = new RecordingWhatsApp();
        var email = new RecordingEmail();
        var settings = new Auth.FakeAppSettings { WhatsAppEnabled = false };
        var provider = BuildReminderProvider(whatsapp, email, settings);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, WhatsappEnabled = true });
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, PlanId = db.Plans.Local.First().Id, Name = "Aqua Co", Subdomain = "co",
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9", Status = TenantStatus.Active
            });
            db.Products.Add(new Product
            {
                Id = productId, TenantId = tenantId, Name = "20L", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
            });
            var cust = Guid.NewGuid();
            db.Customers.Add(new Customer { Id = cust, TenantId = tenantId, Name = "Event Cust", Mobile = "99999", Email = "cust@x.com" });
            AddOrder(db, tenantId, cust, productId, tomorrow, OrderType.Advance, OrderStatus.Confirmed, 200);
            await db.SaveChangesAsync();
        }

        await provider.GetRequiredService<AdvanceOrderReminderJob>().ExecuteAsync(CancellationToken.None);

        Assert.Empty(whatsapp.Sent);              // global switch off → WhatsApp never used despite the plan
        Assert.Single(email.Sent);                // fell back to email
        Assert.Equal("cust@x.com", email.Sent[0].To);
    }

    [Fact]
    public async Task AdvanceOrderReminderJob_AllChannelsDisabled_SendsNothing()
    {
        var whatsapp = new RecordingWhatsApp();
        var email = new RecordingEmail();
        var settings = new Auth.FakeAppSettings { WhatsAppEnabled = false, EmailEnabled = false };
        var provider = BuildReminderProvider(whatsapp, email, settings);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, WhatsappEnabled = true });
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, PlanId = db.Plans.Local.First().Id, Name = "Aqua Co", Subdomain = "co",
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9", Status = TenantStatus.Active
            });
            db.Products.Add(new Product
            {
                Id = productId, TenantId = tenantId, Name = "20L", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
            });
            var cust = Guid.NewGuid();
            db.Customers.Add(new Customer { Id = cust, TenantId = tenantId, Name = "Event Cust", Mobile = "99999", Email = "cust@x.com" });
            AddOrder(db, tenantId, cust, productId, tomorrow, OrderType.Advance, OrderStatus.Confirmed, 200);
            await db.SaveChangesAsync();
        }

        await provider.GetRequiredService<AdvanceOrderReminderJob>().ExecuteAsync(CancellationToken.None);

        Assert.Empty(whatsapp.Sent);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task AdvanceOrderReminderJob_ThrottlesByCadence_DoesNotResendOnSecondRun()
    {
        var whatsapp = new RecordingWhatsApp();
        var provider = BuildReminderProvider(whatsapp);
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, WhatsappEnabled = true });
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, PlanId = db.Plans.Local.First().Id, Name = "Co", Subdomain = "co",
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9", Status = TenantStatus.Active
            });
            db.Products.Add(new Product
            {
                Id = productId, TenantId = tenantId, Name = "20L", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
            });
            var cust = Guid.NewGuid();
            db.Customers.Add(new Customer { Id = cust, TenantId = tenantId, Name = "Event Cust", Mobile = "99999" });
            AddOrder(db, tenantId, cust, productId, tomorrow, OrderType.Advance, OrderStatus.Confirmed, 200);
            await db.SaveChangesAsync();
        }

        var job = provider.GetRequiredService<AdvanceOrderReminderJob>();
        await job.ExecuteAsync(CancellationToken.None);
        await job.ExecuteAsync(CancellationToken.None);   // second run same day — must be throttled

        Assert.Single(whatsapp.Sent);   // reminded once, not twice
    }

    private static void AddOrder(
        AppDbContext db, Guid tenantId, Guid customerId, Guid productId,
        DateOnly date, OrderType type, OrderStatus status, int qty)
    {
        var orderId = Guid.NewGuid();
        db.Orders.Add(new Order
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId,
            OrderDate = date, OrderType = type, Status = status
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderId = orderId,
            ProductId = productId, Quantity = qty, UnitRate = 40m
        });
    }

    [Fact]
    public async Task MonthlyBillingJob_GeneratesInvoicesForAllMonthlyCustomers()
    {
        var provider = BuildProvider();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        // Seed in a scope pinned to the tenant.
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro });
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, PlanId = db.Plans.Local.First().Id, Name = "Co", Subdomain = "co",
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9", Status = TenantStatus.Active
            });
            db.Customers.Add(new Customer
            {
                Id = customerId, TenantId = tenantId, Name = "Monthly Cust", Mobile = "1",
                PaymentPreference = PaymentPreference.Monthly
            });
            db.Products.Add(new Product
            {
                Id = productId, TenantId = tenantId, Name = "20L", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
            });

            // A delivered order in the PREVIOUS month.
            var lastMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-1).AddDays(4);
            var order = new Order
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId,
                OrderDate = lastMonth, Status = OrderStatus.Delivered
            };
            order.OrderItems.Add(new OrderItem
            {
                Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id, ProductId = productId,
                Quantity = 10, UnitRate = 40m
            });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        await provider.GetRequiredService<MonthlyBillingJob>().ExecuteAsync(CancellationToken.None);

        using (var verify = provider.CreateScope())
        {
            verify.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = tenantId;
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoices = await db.Invoices.Where(i => i.CustomerId == customerId).ToListAsync();
            Assert.Single(invoices);
            Assert.Equal(400m, invoices[0].SubTotal);   // 10 * 40
        }
    }

    [Fact]
    public async Task SubscriptionExpiryJob_30DaysOverdue_SuspendsTenant()
    {
        var provider = BuildProvider();
        var tenantId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Priced plan, no discount → net > 0, so this is a genuine unpaid lapse (not a free plan
            // that would auto-renew). It should be suspended after 30+ days overdue.
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MonthlyPrice = 999m });
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, PlanId = db.Plans.Local.First().Id, Name = "Overdue Co", Subdomain = "od",
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
                Status = TenantStatus.Overdue,
                SubscriptionEndsAt = DateTime.UtcNow.AddDays(-31)
            });
            await db.SaveChangesAsync();
        }

        await provider.GetRequiredService<SubscriptionExpiryJob>().ExecuteAsync(CancellationToken.None);

        using (var verify = provider.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantId);
            Assert.Equal(TenantStatus.Suspended, tenant.Status);
        }
    }

    /// <summary>
    /// Seeds one Overdue tenant with the given end dates, runs the expiry job, and reports its status.
    /// A never-paid trial has no SubscriptionEndsAt, which is what selects the shorter suspend window.
    /// </summary>
    private static async Task<TenantStatus> RunExpiryJobForOverdueTenantAsync(
        DateTime? subscriptionEndsAt, DateTime? trialEndsAt)
    {
        var provider = BuildProvider();
        var tenantId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MonthlyPrice = 999m });
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, PlanId = db.Plans.Local.First().Id, Name = "Lapsed Co", Subdomain = "lapsed",
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
                Status = TenantStatus.Overdue,
                SubscriptionEndsAt = subscriptionEndsAt,
                TrialEndsAt = trialEndsAt
            });
            await db.SaveChangesAsync();
        }

        await provider.GetRequiredService<SubscriptionExpiryJob>().ExecuteAsync(CancellationToken.None);

        using var verify = provider.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await verifyDb.Tenants.FirstAsync(t => t.Id == tenantId)).Status;
    }

    /// <summary>
    /// Runs the expiry job for one tenant whose window is closing, and returns the reminder email.
    /// No template rows are seeded, so this exercises the job's own fallback wording and the tokens it
    /// supplies — the template body is the same sentence.
    /// </summary>
    private static async Task<(string Subject, string Body)> ExpiryReminderForAsync(
        DateTime? trialEndsAt, DateTime? subscriptionEndsAt, TenantStatus status)
    {
        var email = new RecordingEmail();
        var provider = BuildProvider(email);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Basic", PlanType = PlanType.Basic, MonthlyPrice = 999m });
            db.Tenants.Add(new Tenant
            {
                Id = Guid.NewGuid(), PlanId = db.Plans.Local.First().Id, Name = "Akash Water", Subdomain = "akash",
                OwnerName = "Akash", OwnerEmail = "akash@x.com", OwnerMobile = "9",
                Status = status, TrialEndsAt = trialEndsAt, SubscriptionEndsAt = subscriptionEndsAt,
            });
            await db.SaveChangesAsync();
        }

        await provider.GetRequiredService<SubscriptionExpiryJob>().ExecuteAsync(CancellationToken.None);

        // A paid tenant this close to expiry is also inside the invoice lead window, so the job emails
        // the renewal invoice too. Pick the reminder by subject rather than assuming a single send.
        var sent = email.Sent.First(e => !e.Subject.Contains("invoice", StringComparison.OrdinalIgnoreCase));
        return (sent.Subject, sent.Body);
    }

    [Fact]
    public async Task ExpiryReminder_NamesTheActualEndDate_NotTheConfiguredWarningWindow()
    {
        // The bug: {{Days}} was filled with Jobs:SubscriptionExpiryWarnDays (7), so an owner two days
        // from expiry was still told "expires within 7 days" — on every reminder in the window.
        var endsAt = DateTime.UtcNow.AddDays(2);

        var (subject, body) = await ExpiryReminderForAsync(null, endsAt, TenantStatus.Active);

        var expected = endsAt.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(expected, body);
        Assert.Contains(expected, subject);
        Assert.DoesNotContain("7 days", body);
    }

    [Fact]
    public async Task ExpiryReminder_TellsATrialOwnerItIsATrial_NotASubscriptionToRenew()
    {
        // A trial owner bought nothing, so "your subscription … please renew" is meaningless to them.
        var (subject, body) = await ExpiryReminderForAsync(DateTime.UtcNow.AddDays(3), null, TenantStatus.Trial);

        Assert.Contains("free trial", body);
        Assert.Contains("free trial", subject);
        Assert.DoesNotContain("renew", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpiryReminder_CallsAPaidTenantsTermASubscription()
    {
        var (_, body) = await ExpiryReminderForAsync(
            DateTime.UtcNow.AddDays(-90), DateTime.UtcNow.AddDays(3), TenantStatus.Active);

        // The stale trial date every paying tenant carries must not make this read as a trial notice.
        Assert.Contains("subscription", body);
        Assert.DoesNotContain("trial", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubscriptionExpiryJob_NeverPaidTrial_SuspendsAfterTheShorterTrialWindow()
    {
        // 8 days past trial end: already suspended on the 7-day trial window, but would still be
        // waiting on the 30-day paid window — this is the case the separate knob exists for.
        var status = await RunExpiryJobForOverdueTenantAsync(
            subscriptionEndsAt: null, trialEndsAt: DateTime.UtcNow.AddDays(-8));

        Assert.Equal(TenantStatus.Suspended, status);
    }

    [Fact]
    public async Task SubscriptionExpiryJob_NeverPaidTrial_StaysOverdueInsideTheTrialWindow()
    {
        var status = await RunExpiryJobForOverdueTenantAsync(
            subscriptionEndsAt: null, trialEndsAt: DateTime.UtcNow.AddDays(-3));

        Assert.Equal(TenantStatus.Overdue, status);
    }

    [Fact]
    public async Task SubscriptionExpiryJob_PaidLapse_KeepsTheLongerWindow_EvenPastTheTrialOne()
    {
        // A paying customer 10 days late must NOT be caught by the trial window — they get the full
        // 30-day dunning month. The stale TrialEndsAt from their signup must not shorten it either.
        var status = await RunExpiryJobForOverdueTenantAsync(
            subscriptionEndsAt: DateTime.UtcNow.AddDays(-10), trialEndsAt: DateTime.UtcNow.AddDays(-400));

        Assert.Equal(TenantStatus.Overdue, status);
    }

    [Fact]
    public async Task SubscriptionExpiryJob_NoEndDatesAtAll_IsNeverSuspended()
    {
        var status = await RunExpiryJobForOverdueTenantAsync(subscriptionEndsAt: null, trialEndsAt: null);

        Assert.Equal(TenantStatus.Overdue, status);
    }

    [Fact]
    public async Task MonthlyBillingJob_IsIdempotent_NoDuplicateInvoiceOnRerun()
    {
        var provider = BuildProvider();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro });
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, PlanId = db.Plans.Local.First().Id, Name = "Co", Subdomain = "co",
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9", Status = TenantStatus.Active
            });
            db.Customers.Add(new Customer
            {
                Id = customerId, TenantId = tenantId, Name = "Monthly Cust", Mobile = "1",
                PaymentPreference = PaymentPreference.Monthly
            });
            db.Products.Add(new Product
            {
                Id = productId, TenantId = tenantId, Name = "20L", BottleSize = BottleSize.TwentyL, DefaultRate = 40m
            });
            var lastMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-1).AddDays(4);
            var order = new Order
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId,
                OrderDate = lastMonth, Status = OrderStatus.Delivered
            };
            order.OrderItems.Add(new OrderItem
            {
                Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id, ProductId = productId,
                Quantity = 10, UnitRate = 40m
            });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        var job = provider.GetRequiredService<MonthlyBillingJob>();
        await job.ExecuteAsync(CancellationToken.None);
        await job.ExecuteAsync(CancellationToken.None);   // second run must not duplicate the invoice

        using (var verify = provider.CreateScope())
        {
            verify.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = tenantId;
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoices = await db.Invoices.Where(i => i.CustomerId == customerId).ToListAsync();
            Assert.Single(invoices);   // still exactly one invoice for the period, not two
        }
    }

    [Fact]
    public async Task SubscriptionExpiryJob_ThrottlesReminderEmail_OnRerun()
    {
        var provider = BuildProvider();
        var tenantId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MonthlyPrice = 999m });
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, PlanId = db.Plans.Local.First().Id, Name = "Expiring Co", Subdomain = "ex",
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
                Status = TenantStatus.Active,
                SubscriptionEndsAt = DateTime.UtcNow.AddDays(3)   // inside the 7-day warn window, still future
            });
            await db.SaveChangesAsync();
        }

        var job = provider.GetRequiredService<SubscriptionExpiryJob>();
        await job.ExecuteAsync(CancellationToken.None);
        await job.ExecuteAsync(CancellationToken.None);   // second run same day must be throttled

        using (var verify = provider.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            var logs = await db.ReminderLogs.IgnoreQueryFilters()
                .Where(r => r.ReminderType == ReminderTypes.SubscriptionExpiry).ToListAsync();
            Assert.Single(logs);   // reminded once, not on every run
        }
    }

    [Fact]
    public async Task SubscriptionExpiryJob_RaisesOnePendingRenewalInvoice_IdempotentOnRerun()
    {
        var provider = BuildProvider();
        var tenantId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MonthlyPrice = 999m, YearlyPrice = 9990m });
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, PlanId = db.Plans.Local.First().Id, Name = "Renew Co", Subdomain = "rn",
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
                Status = TenantStatus.Active,
                SubscriptionEndsAt = DateTime.UtcNow.AddDays(3)   // inside the 5-day invoice lead window
            });
            await db.SaveChangesAsync();
        }

        var job = provider.GetRequiredService<SubscriptionExpiryJob>();
        await job.ExecuteAsync(CancellationToken.None);
        await job.ExecuteAsync(CancellationToken.None);   // rerun must not create a second invoice

        using (var verify = provider.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoices = await db.SubscriptionInvoices.Where(i => i.TenantId == tenantId).ToListAsync();
            Assert.Single(invoices);
            Assert.Equal("Pending", invoices[0].Status);
            Assert.Equal(999m, invoices[0].Amount);
        }
    }

    [Fact]
    public async Task SubscriptionExpiryJob_AutoRenewsFreeTenants()
    {
        var provider = BuildProvider();
        var tenantId = Guid.NewGuid();
        var before = DateTime.UtcNow.AddDays(3);   // inside the lead window, still future

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro, MonthlyPrice = 999m });
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, PlanId = db.Plans.Local.First().Id, Name = "Free Co", Subdomain = "fr",
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
                Status = TenantStatus.Overdue,
                SubscriptionEndsAt = before,
                SubscriptionDiscountType = SubscriptionDiscountType.Percentage,
                SubscriptionDiscountValue = 100m   // fully discounted → net ₹0 → auto-renew, no payment
            });
            await db.SaveChangesAsync();
        }

        await provider.GetRequiredService<SubscriptionExpiryJob>().ExecuteAsync(CancellationToken.None);

        using (var verify = provider.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantId);
            Assert.Equal(TenantStatus.Active, tenant.Status);                    // recovered, not suspended
            Assert.True(tenant.SubscriptionEndsAt > before.AddDays(20));         // rolled forward ~1 month
            var invoices = await db.SubscriptionInvoices.Where(i => i.TenantId == tenantId).ToListAsync();
            Assert.Single(invoices);
            Assert.Equal("Paid", invoices[0].Status);                           // ₹0 Paid record, not Pending
            Assert.Equal(0m, invoices[0].Amount);
        }
    }

    [Fact]
    public async Task TenantJobRunner_IncludesOverdueWithinGrace_SkipsPastGraceAndSuspended()
    {
        var provider = BuildProvider();
        var active = Guid.NewGuid();
        var overdueInGrace = Guid.NewGuid();
        var overduePastGrace = Guid.NewGuid();
        var suspended = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Pro", PlanType = PlanType.Pro });
            var planId = db.Plans.Local.First().Id;

            Tenant Make(Guid id, string sub, TenantStatus status, DateTime? endsAt) => new()
            {
                Id = id, PlanId = planId, Name = sub, Subdomain = sub,
                OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9",
                Status = status, SubscriptionEndsAt = endsAt
            };

            db.Tenants.Add(Make(active, "a", TenantStatus.Active, DateTime.UtcNow.AddDays(20)));
            // Overdue but only 3 days past due — inside the default 7-day overdue grace → still operational.
            db.Tenants.Add(Make(overdueInGrace, "b", TenantStatus.Overdue, DateTime.UtcNow.AddDays(-3)));
            // Overdue 10 days past due — beyond grace (paywall armed) → skipped.
            db.Tenants.Add(Make(overduePastGrace, "c", TenantStatus.Overdue, DateTime.UtcNow.AddDays(-10)));
            db.Tenants.Add(Make(suspended, "d", TenantStatus.Suspended, DateTime.UtcNow.AddDays(-40)));
            await db.SaveChangesAsync();
        }

        var visited = new List<Guid>();
        await provider.GetRequiredService<TenantJobRunner>()
            .ForEachTenantAsync((_, tenantId, _) => { visited.Add(tenantId); return Task.CompletedTask; });

        Assert.Contains(active, visited);
        Assert.Contains(overdueInGrace, visited);
        Assert.DoesNotContain(overduePastGrace, visited);
        Assert.DoesNotContain(suspended, visited);
    }
}
