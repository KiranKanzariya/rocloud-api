using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Users;

/// <summary>
/// Shared team-member creation used by CreateUser and InviteUser: validates email uniqueness,
/// role membership, the plan's user limit and the area assignments; generates a temporary
/// password; and writes the user + its user_areas rows. The caller commits and sends the email.
/// </summary>
internal static class UserProvisioning
{
    private const string PasswordAlphabet =
        "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789@#$%&*";

    public static async Task<(User User, string TempPassword)> CreateAsync(
        IAppDbContext db,
        ITenantContext tenant,
        IPasswordService passwords,
        string name,
        string? mobile,
        string email,
        Guid roleId,
        string? preferredLanguage,
        IReadOnlyList<Guid>? areaIds,
        CancellationToken ct)
    {
        // Email uniqueness within the tenant (case-insensitive).
        var emailTaken = await db.Users.AnyAsync(
            u => u.Email != null && u.Email.ToLower() == email.ToLower(), ct);
        if (emailTaken)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["email"] = ["A team member with this email already exists."]
            });

        // Role must exist in the tenant.
        var roleName = await db.Roles.Where(r => r.Id == roleId).Select(r => r.Name).FirstOrDefaultAsync(ct);
        if (roleName is null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["roleId"] = ["The selected role does not exist."]
            });

        await EnforcePlanLimitAsync(db, tenant, ct);
        if (roleName == "DeliveryBoy")
            await Subscription.PlanLimits.EnsureCanAddDeliveryBoyAsync(db, tenant, ct);
        await ValidateAreasAsync(db, areaIds, ct);

        var tempPassword = GenerateTempPassword();
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            RoleId = roleId,
            Name = name,
            Mobile = mobile,
            Email = email,
            PasswordHash = passwords.Hash(tempPassword),
            AuthProvider = AuthProvider.Custom,
            PreferredLanguage = preferredLanguage,
            IsActive = true
        };
        db.Users.Add(user);

        if (areaIds is { Count: > 0 })
            foreach (var areaId in areaIds.Distinct())
                db.UserAreas.Add(new UserArea
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.TenantId,
                    UserId = user.Id,
                    AreaId = areaId
                });

        return (user, tempPassword);
    }

    /// <summary>Throws when adding a user would exceed the tenant plan's MaxUsers.</summary>
    public static async Task EnforcePlanLimitAsync(IAppDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        var plan = await db.Tenants
            .Where(t => t.Id == tenant.TenantId)
            .Select(t => new { t.Plan!.Name, t.Plan.MaxUsers })
            .FirstOrDefaultAsync(ct);

        if (plan is null) return;   // no plan info — don't block
        if (plan.MaxUsers == Plan.Unlimited) return;   // 0 = unlimited (e.g. Enterprise) — no cap

        var userCount = await db.Users.CountAsync(ct);   // query filter scopes to tenant, excludes soft-deleted
        if (userCount >= plan.MaxUsers)
            throw new PlanLimitException($"Upgrade required: max {plan.MaxUsers} users on the {plan.Name} plan.");
    }

    /// <summary>Validates that every supplied area id belongs to the current tenant.</summary>
    public static async Task ValidateAreasAsync(IAppDbContext db, IReadOnlyList<Guid>? areaIds, CancellationToken ct)
    {
        if (areaIds is not { Count: > 0 }) return;

        var distinct = areaIds.Distinct().ToList();
        var found = await db.Areas.CountAsync(a => distinct.Contains(a.Id), ct);
        if (found != distinct.Count)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["areaIds"] = ["One or more areas do not exist in this tenant."]
            });
    }

    private static string GenerateTempPassword(int length = 14)
        => RandomNumberGenerator.GetString(PasswordAlphabet, length);
}
