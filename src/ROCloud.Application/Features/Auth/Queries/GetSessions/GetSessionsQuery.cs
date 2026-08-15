using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Auth.Queries.GetSessions;

/// <summary>One signed-in device, as shown in "Signed-in devices".</summary>
/// <param name="Id">The session id — what <see cref="Commands.RevokeSession.RevokeSessionCommand"/> takes.</param>
/// <param name="Label">Device name as the client reported it, or null for a session predating labels.</param>
/// <param name="SignedInAt">When this device first signed in (the start of its rotation chain).</param>
/// <param name="LastSeenAt">Last time it exchanged its refresh token — roughly hourly while in use.</param>
/// <param name="ExpiresAt">When it will drop out on its own without being used.</param>
/// <param name="IsCurrent">True for the device making this request.</param>
public sealed record SessionDto(
    Guid Id, string? Label, DateTime SignedInAt, DateTime? LastSeenAt, DateTime ExpiresAt, bool IsCurrent);

/// <summary>
/// Lists the caller's own signed-in devices.
/// </summary>
/// <remarks>
/// This is the visibility half of the sessions table. The rows have existed for a while and are what
/// let a phone and a portal stay signed in independently — but nothing ever showed them, so an owner
/// could not tell whether a second session existed, let alone end it. That is the whole question
/// raised by app cloning: an OEM dual-app copy, a work profile, or someone who had the handset for ten
/// minutes each produce a perfectly ordinary extra session, and the only defence is being able to see
/// it and press a button.
/// </remarks>
public sealed record GetSessionsQuery : IRequest<IReadOnlyList<SessionDto>>;

public class GetSessionsQueryHandler : IRequestHandler<GetSessionsQuery, IReadOnlyList<SessionDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetSessionsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<SessionDto>> Handle(GetSessionsQuery request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            throw new ForbiddenAccessException("Not signed in.");

        var now = DateTime.UtcNow;

        // Live rows only, scoped to the caller. user_sessions carries no tenant query filter by design
        // (refresh runs where no tenant is resolved), so the user scope here is the isolation — never
        // widen this to a tenant-wide or unscoped read.
        var rows = await _db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .Select(s => new { s.SessionId, s.Label, s.CreatedAt, s.LastSeenAt, s.ExpiresAt })
            .ToListAsync(ct);

        // One entry per DEVICE, not per row: rotation writes a new row every refresh under the same
        // SessionId, so a phone signed in for a week would otherwise appear ~168 times. The chain's
        // first row is when the device signed in; its last is what the device is using now.
        return rows
            .GroupBy(s => s.SessionId)
            .Select(g => new SessionDto(
                g.Key,
                g.OrderByDescending(s => s.CreatedAt).Select(s => s.Label).FirstOrDefault(l => l != null),
                g.Min(s => s.CreatedAt),
                g.Max(s => s.LastSeenAt),
                g.Max(s => s.ExpiresAt),
                g.Key == _currentUser.SessionId))
            .OrderByDescending(s => s.IsCurrent)
            .ThenByDescending(s => s.LastSeenAt ?? s.SignedInAt)
            .ToList();
    }
}
