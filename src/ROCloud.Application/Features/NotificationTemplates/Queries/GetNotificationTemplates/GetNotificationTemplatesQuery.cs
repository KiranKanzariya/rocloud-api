using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.NotificationTemplates.Dtos;

namespace ROCloud.Application.Features.NotificationTemplates.Queries.GetNotificationTemplates;

/// <summary>
/// Lists the tenant's EFFECTIVE notification templates (optionally filtered by channel): for each
/// (templateCode, languageCode, channel) the tenant's own override if it exists, otherwise the shared
/// system default (tenant_id NULL). This mirrors the send-path precedence in
/// INotificationTemplateRenderer, so a non-technical owner sees the working default pre-filled and
/// only creates an override when they actually edit one. Platform-only templates (rendered with
/// tenant_id NULL only, e.g. subscription_expiry) are hidden.
/// </summary>
public sealed record GetNotificationTemplatesQuery(string? Channel = null)
    : IRequest<IReadOnlyList<NotificationTemplateDto>>;

public class GetNotificationTemplatesQueryHandler
    : IRequestHandler<GetNotificationTemplatesQuery, IReadOnlyList<NotificationTemplateDto>>
{
    /// <summary>Templates the platform sends to the tenant owner, not the tenant to its customers — hidden here.</summary>
    private static readonly string[] PlatformOnlyCodes = ["subscription_expiry"];

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public GetNotificationTemplatesQueryHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<NotificationTemplateDto>> Handle(
        GetNotificationTemplatesQuery request, CancellationToken ct)
    {
        var tenantId = _tenant.TenantId;

        // NotificationTemplate is not tenant-filtered globally (nullable tenant_id) — scope here to
        // this tenant's overrides plus the shared defaults.
        var rows = await _db.NotificationTemplates.AsNoTracking()
            .Where(t => (t.TenantId == tenantId || t.TenantId == null)
                        && !PlatformOnlyCodes.Contains(t.TemplateCode)
                        && (request.Channel == null || t.Channel == request.Channel))
            .ToListAsync(ct);

        // Effective row per key: the tenant's override wins over the system default.
        return rows
            .GroupBy(t => new { t.TemplateCode, t.LanguageCode, t.Channel })
            .Select(g =>
            {
                var tenantRow = g.FirstOrDefault(x => x.TenantId == tenantId);
                var chosen = tenantRow ?? g.First();   // no override → all rows in the group are defaults
                return new NotificationTemplateDto(
                    chosen.Id, chosen.TemplateCode, chosen.LanguageCode, chosen.Channel,
                    chosen.Subject, chosen.Body, chosen.UpdatedAt, tenantRow is not null);
            })
            .OrderBy(d => d.Channel).ThenBy(d => d.TemplateCode).ThenBy(d => d.LanguageCode)
            .ToList();
    }
}
