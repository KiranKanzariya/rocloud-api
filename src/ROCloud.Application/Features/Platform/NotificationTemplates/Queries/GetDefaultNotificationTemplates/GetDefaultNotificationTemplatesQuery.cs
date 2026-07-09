using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.NotificationTemplates.Dtos;

namespace ROCloud.Application.Features.Platform.NotificationTemplates.Queries.GetDefaultNotificationTemplates;

/// <summary>
/// Lists the system-default notification templates (tenant_id IS NULL) — the shared baseline every
/// tenant inherits until it overrides a template in its own portal. Platform/super-admin use;
/// reuses the tenant <see cref="NotificationTemplateDto"/> shape.
/// </summary>
public sealed record GetDefaultNotificationTemplatesQuery(string? Channel = null)
    : IRequest<IReadOnlyList<NotificationTemplateDto>>;

public class GetDefaultNotificationTemplatesQueryHandler
    : IRequestHandler<GetDefaultNotificationTemplatesQuery, IReadOnlyList<NotificationTemplateDto>>
{
    private readonly IAppDbContext _db;

    public GetDefaultNotificationTemplatesQueryHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<NotificationTemplateDto>> Handle(
        GetDefaultNotificationTemplatesQuery request, CancellationToken ct)
    {
        var query = _db.NotificationTemplates.AsNoTracking()
            .Where(t => t.TenantId == null);   // system defaults only

        if (!string.IsNullOrWhiteSpace(request.Channel))
            query = query.Where(t => t.Channel == request.Channel);

        return await query
            .OrderBy(t => t.Channel).ThenBy(t => t.TemplateCode).ThenBy(t => t.LanguageCode)
            .Select(t => new NotificationTemplateDto(
                t.Id, t.TemplateCode, t.LanguageCode, t.Channel, t.Subject, t.Body, t.UpdatedAt, false))
            .ToListAsync(ct);
    }
}
