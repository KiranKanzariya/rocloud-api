using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Common.Settings;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.Caching;
using ROCloud.Infrastructure.Identity;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Auth;

/// <summary>Deterministic fake token service so refresh-hash comparisons are stable in tests.</summary>
public sealed class FakeTokenService : ITokenService
{
    public GeneratedAccessToken GenerateAccessToken(User user, Tenant tenant, IReadOnlyCollection<string> permissions)
        => new("access-token", DateTime.UtcNow.AddMinutes(60));

    public GeneratedAccessToken GeneratePlatformToken(PlatformUser platformUser)
        => new("platform-access-token", DateTime.UtcNow.AddMinutes(60));

    public string GenerateRefreshToken() => Guid.NewGuid().ToString("N");

    public string HashRefreshToken(string refreshToken) => "H:" + refreshToken;

    /// <summary>Deterministic, round-trippable handoff token: "handoff:{userId}:{tenantId}".</summary>
    public string GenerateHandoffToken(Guid userId, Guid tenantId) => $"handoff:{userId}:{tenantId}";

    public HandoffPayload? ValidateHandoffToken(string token)
    {
        var parts = token.Split(':');
        return parts is ["handoff", var u, var t]
            && Guid.TryParse(u, out var userId)
            && Guid.TryParse(t, out var tenantId)
                ? new HandoffPayload(userId, tenantId)
                : null;
    }
}

/// <summary>Default operational settings for tests (mirrors the production fallbacks).</summary>
public sealed class FakeAppSettings : IAppSettings
{
    public string WebUrl => "https://app.test";
    public string TimeZone => "Asia/Kolkata";
    public string TenantUrlFormat => "https://{subdomain}.app.test";
    public int RefreshTokenExpiryDays => 30;
    public int TrialDays => 14;
    public int MaxLoginAttempts => 5;
    public int LockoutMinutes => 15;
    public int PasswordResetTokenTtlMinutes => 60;
    public decimal DefaultGstRate => 0.18m;
    public int InvoiceDueInDays => 15;
    public int InvoiceLinkExpiryDays => 7;
    public long DeliveryProofMaxBytes => 5 * 1024 * 1024;
    // Notification channel toggles default on; tests flip them via object initializer.
    public bool EmailEnabled { get; init; } = true;
    public bool SmsEnabled { get; init; } = true;
    public bool WhatsAppEnabled { get; init; } = true;
    // Per-event customer-notification toggles default on (matches the production "missing key = true").
    public bool InvoiceSentEnabled { get; init; } = true;
    public bool PaymentReminderEnabled { get; init; } = true;
    public bool AmcReminderEnabled { get; init; } = true;
    public bool AdvanceOrderReminderEnabled { get; init; } = true;
    public int SubscriptionInvoiceLeadDays { get; init; } = 5;
    public int SubscriptionOverdueGraceDays { get; init; } = 7;
}

public static class AuthTestHelpers
{
    public const string Subdomain = "acme";
    public const string OwnerEmail = "owner@acme.test";
    public const string ValidPassword = "ValidPass123!";

    public static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"auth-tests-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options, new TenantContext());
    }

    public static InMemoryCacheService NewCache() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    /// <summary>Seeds a plan, an active tenant, an Owner role/permission and an Owner user.</summary>
    public static async Task<(Tenant Tenant, User Owner)> SeedAsync(AppDbContext db)
    {
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Pro",
            PlanType = PlanType.Pro,
            MonthlyPrice = 2499m,
            YearlyPrice = 24990m,
            IsActive = true
        };
        db.Plans.Add(plan);

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            PlanId = plan.Id,
            Plan = plan,
            Name = "Acme Water",
            Subdomain = Subdomain,
            OwnerName = "Owner",
            OwnerEmail = OwnerEmail,
            OwnerMobile = "9999999999",
            Status = TenantStatus.Active,
            DefaultLanguage = "en"
        };
        db.Tenants.Add(tenant);

        var permission = new Permission { Id = Guid.NewGuid(), Module = "Customers", Action = "View", Code = "Customers.View" };
        db.Permissions.Add(permission);

        var role = new Role { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Owner", IsSystem = true };
        db.Roles.Add(role);
        db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });

        var owner = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            RoleId = role.Id,
            Name = "Owner",
            Email = OwnerEmail,
            PasswordHash = new PasswordService(new ConfigurationBuilder().Build()).Hash(ValidPassword),
            AuthProvider = AuthProvider.Custom,
            IsActive = true
        };
        db.Users.Add(owner);

        await db.SaveChangesAsync();
        return (tenant, owner);
    }
}
