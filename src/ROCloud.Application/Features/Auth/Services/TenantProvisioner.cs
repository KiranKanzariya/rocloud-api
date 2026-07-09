using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Auth.Services;

/// <summary>Result of provisioning: the new Owner user and the permission codes it holds.</summary>
public sealed record ProvisionResult(User Owner, IReadOnlyCollection<string> OwnerPermissions);

/// <summary>
/// Provisions a brand-new tenant: the seven system roles with their permission grants,
/// the Owner user, and the default 20L/18L products (guide Phase 5 / Phase 9).
/// Entities are added to the context; the caller commits the transaction.
/// </summary>
public class TenantProvisioner
{
    private readonly IAppDbContext _db;

    public TenantProvisioner(IAppDbContext db) => _db = db;

    // System role → granted permission codes. "*" grants every permission.
    private static readonly Dictionary<string, string[]> RoleMatrix = new()
    {
        ["Owner"] = ["*"],
        ["Manager"] =
        [
            "Customers.View", "Customers.Create", "Customers.Edit", "Customers.Delete",
            "Orders.View", "Orders.Create", "Orders.Edit", "Orders.Cancel",
            "Deliveries.View", "Deliveries.Update",
            "Inventory.View", "Inventory.Manage",
            "Invoices.View", "Invoices.Create", "Invoices.Edit",
            "Payments.View", "Payments.Collect", "Payments.Manage",
            "Reports.View",
            "AMC.View", "AMC.Manage", "AMC.Update",
            "Users.View", "Settings.View"
        ],
        ["DeliveryBoy"] =
        [
            "Deliveries.ViewOwn", "Deliveries.Update", "Orders.View", "Customers.View", "Payments.Collect"
        ],
        ["Accountant"] =
        [
            "Customers.View", "Orders.View",
            "Invoices.View", "Invoices.Create", "Invoices.Edit",
            "Payments.View", "Payments.Collect", "Payments.Manage",
            "Reports.View"
        ],
        ["Technician"] = ["Customers.View", "AMC.View", "AMC.Update"],
        ["CustomerCare"] =
        [
            "Customers.View", "Customers.Create", "Customers.Edit",
            "Orders.View", "Orders.Create", "Deliveries.View", "Payments.View",
            "AMC.View", "AMC.Manage"
        ],
        ["Viewer"] =
        [
            "Customers.View", "Orders.View", "Deliveries.View", "Inventory.View",
            "Invoices.View", "Payments.View", "Reports.View", "AMC.View", "Users.View", "Settings.View"
        ]
    };

    public async Task<ProvisionResult> ProvisionAsync(
        Tenant tenant, string ownerName, string ownerEmail, string ownerPasswordHash, string ownerMobile,
        CancellationToken ct)
    {
        var allPermissions = await _db.Permissions.ToListAsync(ct);
        var byCode = allPermissions.ToDictionary(p => p.Code, p => p);

        Role? ownerRole = null;
        foreach (var (roleName, codes) in RoleMatrix)
        {
            var role = new Role
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                Name = roleName,
                IsSystem = true,
                IsCustom = false
            };
            _db.Roles.Add(role);

            var grantedCodes = codes.Contains("*") ? allPermissions.Select(p => p.Code) : codes;
            foreach (var code in grantedCodes)
            {
                if (byCode.TryGetValue(code, out var permission))
                    _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }

            if (roleName == "Owner") ownerRole = role;
        }

        var owner = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            RoleId = ownerRole!.Id,
            Name = ownerName,
            Email = ownerEmail,
            Mobile = ownerMobile,
            PasswordHash = ownerPasswordHash,
            AuthProvider = AuthProvider.Custom,
            IsActive = true
        };
        _db.Users.Add(owner);

        // Default products (rate configurable later in Settings)
        _db.Products.Add(new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "20L Jar",
            BottleSize = BottleSize.TwentyL,
            DefaultRate = 0m,
            Unit = "bottle",
            IsActive = true
        });
        _db.Products.Add(new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "18L Jar",
            BottleSize = BottleSize.EighteenL,
            DefaultRate = 0m,
            Unit = "bottle",
            IsActive = true
        });

        return new ProvisionResult(owner, allPermissions.Select(p => p.Code).ToArray());
    }
}
