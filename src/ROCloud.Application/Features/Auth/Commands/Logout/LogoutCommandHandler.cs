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
        // Blocklist the access token when there is one. There often is not: sign-out is anonymous
        // precisely so it still works after the access token has expired, and an expired token needs
        // no blocklisting.
        if (_currentUser.Jti is { } jti && _currentUser.AccessTokenExpiresAt is { } expiresAt)
            await _blocklist.BlockAsync(jti, expiresAt, ct);

        if (string.IsNullOrEmpty(request.RefreshToken))
            return;

        // Found by token hash ALONE — deliberately not scoped to the signed-in user. The endpoint is
        // anonymous now, so on an expired access token there is no user to scope to, and requiring one
        // would silently turn sign-out back into "clears the cookie, leaves the session live". The hash
        // is sufficient authority on its own: token_hash is UNIQUE, and only the holder of the token
        // can produce it.
        var hash = _tokens.HashRefreshToken(request.RefreshToken);
        var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);

        if (session is null) return;

        // This device only. Signing out of the phone leaves the portal signed in, which is what an
        // owner expects and what the single-token design could not do.
        await UserSessions.RevokeChainAsync(_db, session.SessionId, ct);
        await _db.SaveChangesAsync(ct);
    }
}
