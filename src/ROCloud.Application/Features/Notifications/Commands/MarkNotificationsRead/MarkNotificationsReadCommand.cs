using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Notifications.Commands.MarkNotificationsRead;

/// <summary>Marks all of the current user's unread notifications as read. Returns the number marked.</summary>
public sealed record MarkNotificationsReadCommand : IRequest<int>;

public class MarkNotificationsReadCommandHandler : IRequestHandler<MarkNotificationsReadCommand, int>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _user;

    public MarkNotificationsReadCommandHandler(IAppDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    public async Task<int> Handle(MarkNotificationsReadCommand request, CancellationToken ct)
    {
        var userId = _user.UserId;
        if (userId is null) return 0;

        var unread = await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(ct);

        foreach (var n in unread)
            n.IsRead = true;

        if (unread.Count > 0)
            await _db.SaveChangesAsync(ct);

        return unread.Count;
    }
}
