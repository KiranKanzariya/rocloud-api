using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;

namespace ROCloud.Application.Features.Auth.Commands.RevokeSession;

/// <summary>
/// Ends one of the caller's own signed-in devices, remotely.
/// </summary>
/// <remarks>
/// Sign-out has only ever been able to end the device you are holding, which is no use in the case
/// that matters: a cloned app, a phone someone else has, a session left open on a shared machine. This
/// is how an owner ends one of those. Deliberately scoped to the caller's OWN sessions — an owner
/// cannot use it on a team member (deactivating them is the tool for that, and it revokes everything).
/// </remarks>
public sealed record RevokeSessionCommand(Guid SessionId) : IRequest;

public class RevokeSessionCommandHandler : IRequestHandler<RevokeSessionCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly SessionValidityService _sessions;

    public RevokeSessionCommandHandler(
        IAppDbContext db, ICurrentUserService currentUser, SessionValidityService sessions)
    {
        _db = db;
        _currentUser = currentUser;
        _sessions = sessions;
    }

    public async Task Handle(RevokeSessionCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            throw new ForbiddenAccessException("Not signed in.");

        // Ownership is checked BEFORE the revoke, against this user's rows. RevokeChainAsync takes a
        // bare SessionId and would happily end anyone's device, so the guard has to be here — a session
        // id is a plain GUID in a URL, and nothing else stands between it and another account.
        var owned = await _db.UserSessions
            .AnyAsync(s => s.SessionId == request.SessionId && s.UserId == userId, ct);

        // Same answer for "not yours" as for "does not exist": a distinguishable 403 would confirm
        // that a guessed session id belongs to somebody.
        if (!owned)
            throw new NotFoundException("Session", request.SessionId);

        await UserSessions.RevokeChainAsync(_db, request.SessionId, ct);
        await _db.SaveChangesAsync(ct);

        // AFTER the save: telling the cache a session is dead before the row says so would, on a
        // failed save, leave a working device locked out with nothing to explain it. This only
        // brings the effect forward — the cache TTL would reach the same answer within a minute.
        await _sessions.MarkSessionRevokedAsync(request.SessionId, ct);
    }
}
