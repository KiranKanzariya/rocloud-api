using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.TenantSettings.Dtos;

namespace ROCloud.Application.Features.TenantSettings.Queries.GetBillingSettings;

/// <summary>Reads only the GST configuration the invoice screens need.</summary>
public sealed record GetBillingSettingsQuery : IRequest<BillingSettingsDto>;

public class GetBillingSettingsQueryHandler : IRequestHandler<GetBillingSettingsQuery, BillingSettingsDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public GetBillingSettingsQueryHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<BillingSettingsDto> Handle(GetBillingSettingsQuery request, CancellationToken ct)
    {
        // Tenants is a platform table (not tenant-filtered) — scope explicitly by the current tenant id.
        // Project only the three columns, but round in memory: GetTenantSettingsQuery does the same,
        // and it keeps the expression off the SQL translator.
        var t = await _db.Tenants.AsNoTracking()
                    .Where(x => x.Id == _tenant.TenantId)
                    .Select(x => new { x.GstEnabled, x.GstRate, x.GstNumber })
                    .FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Tenant", _tenant.TenantId);

        return new BillingSettingsDto(t.GstEnabled, Math.Round(t.GstRate * 100m, 2), t.GstNumber);
    }
}
