using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Platform.Auth.Commands.PlatformLogout;

/// <summary>Revokes the platform user's refresh token (server-side logout).</summary>
public sealed record PlatformLogoutCommand(Guid PlatformUserId) : IRequest;

public class PlatformLogoutCommandHandler : IRequestHandler<PlatformLogoutCommand>
{
    private readonly IAppDbContext _db;

    public PlatformLogoutCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(PlatformLogoutCommand request, CancellationToken ct)
    {
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == request.PlatformUserId, ct);
        if (user is not null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await _db.SaveChangesAsync(ct);
        }
    }
}
