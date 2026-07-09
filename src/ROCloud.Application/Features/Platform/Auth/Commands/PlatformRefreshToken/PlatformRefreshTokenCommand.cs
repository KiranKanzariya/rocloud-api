using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.Auth.Common;
using ROCloud.Application.Features.Platform.Auth.Services;

namespace ROCloud.Application.Features.Platform.Auth.Commands.PlatformRefreshToken;

/// <summary>Rotates a platform refresh token (read from the HttpOnly cookie by the controller).</summary>
public sealed record PlatformRefreshTokenCommand(string RefreshToken) : IRequest<PlatformAuthResult>;

public class PlatformRefreshTokenCommandHandler
    : IRequestHandler<PlatformRefreshTokenCommand, PlatformAuthResult>
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly PlatformTokenIssuer _issuer;

    public PlatformRefreshTokenCommandHandler(IAppDbContext db, ITokenService tokens, PlatformTokenIssuer issuer)
    {
        _db = db;
        _tokens = tokens;
        _issuer = issuer;
    }

    public async Task<PlatformAuthResult> Handle(PlatformRefreshTokenCommand request, CancellationToken ct)
    {
        var dot = request.RefreshToken?.IndexOf('.') ?? -1;
        if (request.RefreshToken is null || dot <= 0
            || !Guid.TryParse(request.RefreshToken[..dot], out var userId))
            throw new InvalidCredentialsException();

        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.RefreshToken is null || !user.IsActive)
            throw new InvalidCredentialsException();

        var presentedHash = _tokens.HashRefreshToken(request.RefreshToken);
        if (!string.Equals(user.RefreshToken, presentedHash, StringComparison.Ordinal))
        {
            // Replay of a rotated token → revoke the session.
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await _db.SaveChangesAsync(ct);
            throw new InvalidCredentialsException();
        }

        if (user.RefreshTokenExpiresAt is null || user.RefreshTokenExpiresAt <= DateTime.UtcNow)
            throw new InvalidCredentialsException();

        return await _issuer.IssueAsync(user, ct);
    }
}
