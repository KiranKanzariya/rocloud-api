using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.Audit.Dtos;

namespace ROCloud.Application.Features.Platform.Audit.Queries.GetAuditSettings;

/// <summary>Reads the single global audit-settings row (SuperAdmin). Falls back to defaults if unset.</summary>
public sealed record GetAuditSettingsQuery : IRequest<AuditSettingsDto>;

public class GetAuditSettingsQueryHandler : IRequestHandler<GetAuditSettingsQuery, AuditSettingsDto>
{
    private readonly IAppDbContext _db;

    public GetAuditSettingsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<AuditSettingsDto> Handle(GetAuditSettingsQuery request, CancellationToken ct)
    {
        var s = await _db.AuditSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (s is null)
        {
            var d = AuditSettingsSnapshot.Defaults();
            return new AuditSettingsDto(d.Enabled, d.CaptureRequestBody, d.MaxRequestBodyBytes,
                d.Methods, d.SensitivePathPrefixes, d.ExcludeModules, d.AuditReadsForModules,
                d.AdditionalRedactKeys, d.RetentionMonths, null);
        }

        return new AuditSettingsDto(
            s.Enabled, s.CaptureRequestBody, s.MaxRequestBodyBytes,
            s.Methods, s.SensitivePathPrefixes, s.ExcludeModules, s.AuditReadsForModules,
            s.AdditionalRedactKeys, s.RetentionMonths, s.UpdatedAt);
    }
}
