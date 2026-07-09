using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.NotificationTemplates.Commands.DeleteNotificationTemplate;

/// <summary>
/// Removes the tenant's OWN override for a template, reverting it to the shared system default. Only a
/// row owned by the current tenant can be deleted — a NULL-tenant default (or another tenant's row) is
/// not matched, so this can never delete the shared baseline.
/// </summary>
public sealed record DeleteNotificationTemplateCommand(Guid Id) : IRequest;

public class DeleteNotificationTemplateCommandHandler : IRequestHandler<DeleteNotificationTemplateCommand>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public DeleteNotificationTemplateCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Handle(DeleteNotificationTemplateCommand request, CancellationToken ct)
    {
        var template = await _db.NotificationTemplates.FirstOrDefaultAsync(
            t => t.Id == request.Id && t.TenantId == _tenant.TenantId, ct)
            ?? throw new NotFoundException("NotificationTemplate", request.Id);

        _db.NotificationTemplates.Remove(template);
        await _db.SaveChangesAsync(ct);
    }
}
