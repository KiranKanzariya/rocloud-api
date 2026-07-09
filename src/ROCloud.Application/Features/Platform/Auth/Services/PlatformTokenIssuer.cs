using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Application.Features.Platform.Auth.Common;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Application.Features.Platform.Auth.Services;

/// <summary>
/// Issues a platform access token + a rotated refresh token, persisting the refresh hash/expiry on
/// the platform_users row. Mirrors AuthTokenIssuer but for staff (no tenant). Refresh token format
/// "{platformUserId}.{random}".
/// </summary>
public class PlatformTokenIssuer
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IAppSettings _settings;

    public PlatformTokenIssuer(IAppDbContext db, ITokenService tokens, IAppSettings settings)
    {
        _db = db;
        _tokens = tokens;
        _settings = settings;
    }

    public async Task<PlatformAuthResult> IssueAsync(PlatformUser user, CancellationToken ct)
    {
        var access = _tokens.GeneratePlatformToken(user);
        var refreshToken = $"{user.Id}.{_tokens.GenerateRefreshToken()}";

        user.RefreshToken = _tokens.HashRefreshToken(refreshToken);
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(_settings.RefreshTokenExpiryDays);
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new PlatformAuthResult(access.Token, access.ExpiresAtUtc, refreshToken);
    }
}
