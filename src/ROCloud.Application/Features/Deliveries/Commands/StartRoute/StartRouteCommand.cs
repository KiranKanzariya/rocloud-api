using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Deliveries.Commands.StartRoute;

/// <summary>
/// Marks the whole of a delivery boy's day In Transit in one call — "I have left the plant".
///
/// <para>In Transit exists for the OWNER: their board has a column for it, and it answers "has the van
/// gone out, where is the route up to". It tells the delivery boy nothing he doesn't already know, so
/// My Route no longer offers it per stop — one tap for the day replaces one tap per house, and on a
/// hundred-stop route that is a hundred modals saved for a status recorded for somebody else.</para>
///
/// <para>Deliberately narrow: it only advances <c>Pending</c> stops for the given day, and only the
/// caller's own. It never touches a completed stop, never moves anything backwards, and can be tapped
/// twice without harm.</para>
/// </summary>
public sealed record StartRouteCommand(DateOnly? Date) : IRequest<int>;

public class StartRouteCommandHandler : IRequestHandler<StartRouteCommand, int>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public StartRouteCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<int> Handle(StartRouteCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new ForbiddenAccessException();
        var date = request.Date ?? AppTimeZone.Today(DateTime.UtcNow);

        // Never a future day: the rollover job writes tomorrow's stops tonight, and the per-stop
        // command already refuses to action those. Starting a route that has not come round yet would
        // put the owner's board into a state the individual updates would then reject.
        if (date > AppTimeZone.Today(DateTime.UtcNow))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["date"] = ["You can't start a route before its day."]
            });

        var stops = await _db.Deliveries
            .Include(d => d.Order)
            .Where(d => d.DeliveryBoyId == userId
                        && d.ScheduledDate == date
                        && d.Status == DeliveryStatus.Pending)
            .ToListAsync(ct);

        var started = 0;
        foreach (var delivery in stops)
        {
            // Plant pickup has no outbound leg — the customer collects, so the van never carries it.
            // The per-stop command rejects InTransit for these outright; skipping keeps a mixed day
            // working instead of failing the whole route on the first pickup order.
            if (delivery.Order?.DeliveryMode == DeliveryMode.PlantPickup) continue;

            delivery.Status = DeliveryStatus.InTransit;
            if (delivery.Order is { } order) order.Status = OrderStatus.InTransit;
            started++;
        }

        if (started > 0) await _db.SaveChangesAsync(ct);
        return started;
    }
}
