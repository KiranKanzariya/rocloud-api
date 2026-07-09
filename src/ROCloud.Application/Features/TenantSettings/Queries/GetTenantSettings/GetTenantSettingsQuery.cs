using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.TenantSettings.Dtos;

namespace ROCloud.Application.Features.TenantSettings.Queries.GetTenantSettings;

/// <summary>Reads the current tenant's business profile.</summary>
public sealed record GetTenantSettingsQuery : IRequest<TenantSettingsDto>;

public class GetTenantSettingsQueryHandler : IRequestHandler<GetTenantSettingsQuery, TenantSettingsDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public GetTenantSettingsQueryHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<TenantSettingsDto> Handle(GetTenantSettingsQuery request, CancellationToken ct)
    {
        // Tenants is a platform table (not tenant-filtered) — scope explicitly by the current tenant id.
        var t = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == _tenant.TenantId, ct)
                ?? throw new NotFoundException("Tenant", _tenant.TenantId);

        return new TenantSettingsDto(
            t.Id, t.Name, t.Subdomain, t.OwnerName, t.OwnerEmail, t.OwnerMobile,
            t.GstNumber, t.GstEnabled, Math.Round(t.GstRate * 100m, 2),
            t.AddressLine, t.City, t.State, t.Pincode,
            t.LogoUrl, t.PrimaryColor, t.DefaultLanguage,
            _tenant.PlanType, t.Status.ToString());
    }
}
