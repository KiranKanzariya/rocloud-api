using ROCloud.Application.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Notifications.Dtos;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Notifications.Queries.GetNotifications;

/// <summary>
/// Returns the current user's notification feed. Before reading, it (re)generates notifications
/// from the tenant's current actionable state so the feed reflects live data. Generation is
/// idempotent per (tenant, user, type): an existing alert is updated in place; its unread flag is
/// reset only when the underlying count changes, so marking-as-read sticks until something moves.
/// </summary>
public sealed record GetNotificationsQuery : IRequest<NotificationFeedDto>;

public class GetNotificationsQueryHandler : IRequestHandler<GetNotificationsQuery, NotificationFeedDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserService _user;

    public GetNotificationsQueryHandler(IAppDbContext db, ITenantContext tenant, ICurrentUserService user)
    {
        _db = db;
        _tenant = tenant;
        _user = user;
    }

    public async Task<NotificationFeedDto> Handle(GetNotificationsQuery request, CancellationToken ct)
    {
        var userId = _user.UserId;
        if (userId is null)
            return new NotificationFeedDto(0, Array.Empty<NotificationDto>());

        await GenerateAsync(userId.Value, ct);

        var rows = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

        var items = rows
            .Select(n => new NotificationDto(
                n.Id, n.Type,
                int.TryParse(n.ReferenceKey, out var c) ? c : 0,
                n.Title, n.Link, n.IsRead, n.CreatedAt))
            .ToList();

        return new NotificationFeedDto(items.Count(i => !i.IsRead), items);
    }

    /// <summary>Reconciles stored notifications with the tenant's current actionable counts.</summary>
    private async Task GenerateAsync(Guid userId, CancellationToken ct)
    {
        var today = AppTimeZone.Today(DateTime.UtcNow);

        // Counts are tenant-scoped automatically by the global query filter.
        var overdueInvoices = await _db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Overdue, ct);
        var pendingOrders = await _db.Orders.CountAsync(o => o.Status == OrderStatus.Pending, ct);
        var amcDue = await _db.AmcSubscriptions.CountAsync(a => a.IsActive && a.NextDueDate <= today, ct);
        var openServices = await _db.ServiceRequests.CountAsync(s => s.Status == ServiceRequestStatus.Open, ct);

        var desired = new List<(string Type, int Count, string Title, string Link)>();
        if (overdueInvoices > 0) desired.Add(("InvoicesOverdue", overdueInvoices, $"{overdueInvoices} invoice(s) overdue", "/invoices"));
        if (pendingOrders > 0) desired.Add(("OrdersPending", pendingOrders, $"{pendingOrders} order(s) pending", "/orders"));
        if (amcDue > 0) desired.Add(("AmcDue", amcDue, $"{amcDue} AMC visit(s) due", "/service-requests"));
        if (openServices > 0) desired.Add(("ServiceOpen", openServices, $"{openServices} open service request(s)", "/service-requests"));

        var existing = await _db.Notifications.Where(n => n.UserId == userId).ToListAsync(ct);
        var desiredTypes = desired.Select(d => d.Type).ToHashSet();
        var changed = false;

        // Drop alerts whose condition no longer applies.
        foreach (var stale in existing.Where(n => !desiredTypes.Contains(n.Type)).ToList())
        {
            _db.Notifications.Remove(stale);
            changed = true;
        }

        foreach (var d in desired)
        {
            var key = d.Count.ToString();
            var row = existing.FirstOrDefault(n => n.Type == d.Type);
            if (row is null)
            {
                _db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenant.TenantId,
                    UserId = userId,
                    Type = d.Type,
                    Title = d.Title,
                    Link = d.Link,
                    ReferenceKey = key,
                    IsRead = false,
                });
                changed = true;
            }
            else if (row.ReferenceKey != key)
            {
                row.Title = d.Title;
                row.Link = d.Link;
                row.ReferenceKey = key;
                row.IsRead = false;
                row.CreatedAt = DateTime.UtcNow; // resurface as the newest item
                changed = true;
            }
        }

        if (changed)
            await _db.SaveChangesAsync(ct);
    }
}
