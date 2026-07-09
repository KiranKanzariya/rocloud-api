using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;

namespace ROCloud.Application.Features.Auth.Commands.Logout;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly TokenBlocklistService _blocklist;

    public LogoutCommandHandler(IAppDbContext db, ICurrentUserService currentUser, TokenBlocklistService blocklist)
    {
        _db = db;
        _currentUser = currentUser;
        _blocklist = blocklist;
    }

    public async Task Handle(LogoutCommand request, CancellationToken ct)
    {
        if (_currentUser.Jti is { } jti && _currentUser.AccessTokenExpiresAt is { } expiresAt)
            await _blocklist.BlockAsync(jti, expiresAt, ct);

        if (_currentUser.UserId is { } userId)
        {
            var user = await _db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is not null)
            {
                user.RefreshToken = null;
                user.RefreshTokenExpiresAt = null;
                await _db.SaveChangesAsync(ct);
            }
        }
    }
}
