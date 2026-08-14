using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;

namespace ROCloud.Application.Features.Auth.Commands.Logout;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ITokenService _tokens;
    private readonly TokenBlocklistService _blocklist;

    public LogoutCommandHandler(
        IAppDbContext db, ICurrentUserService currentUser, ITokenService tokens,
        TokenBlocklistService blocklist)
    {
        _db = db;
        _currentUser = currentUser;
        _tokens = tokens;
        _blocklist = blocklist;
    }

    public async Task Handle(LogoutCommand request, CancellationToken ct)
    {
        if (_currentUser.Jti is { } jti && _currentUser.AccessTokenExpiresAt is { } expiresAt)
            await _blocklist.BlockAsync(jti, expiresAt, ct);

        if (_currentUser.UserId is not { } userId || string.IsNullOrEmpty(request.RefreshToken))
            return;

        // This device only. Signing out of the phone leaves the portal signed in, which is what an
        // owner expects and what the single-token design could not do.
        var hash = _tokens.HashRefreshToken(request.RefreshToken);
        var session = await _db.UserSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.TokenHash == hash, ct);

        if (session is null) return;

        await UserSessions.RevokeChainAsync(_db, session.SessionId, ct);
        await _db.SaveChangesAsync(ct);
    }
}
