using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Application.Features.Auth.Common;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Features.Auth.Services;

/// <summary>
/// Issues an access token + a rotated refresh token and persists the refresh hash/expiry
/// on the user. The refresh token is formatted "{userId}.{random}" so the refresh handler
/// can identify the user and detect reuse of a rotated token (guide §10.3).
/// </summary>
public class AuthTokenIssuer
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IAppSettings _settings;

    public AuthTokenIssuer(IAppDbContext db, ITokenService tokens, IAppSettings settings)
    {
        _db = db;
        _tokens = tokens;
        _settings = settings;
    }

    public async Task<AuthResult> IssueAsync(
        User user, Tenant tenant, IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        var access = _tokens.GenerateAccessToken(user, tenant, permissions);
        var refreshToken = $"{user.Id}.{_tokens.GenerateRefreshToken()}";

        user.RefreshToken = _tokens.HashRefreshToken(refreshToken);
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(_settings.RefreshTokenExpiryDays);
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new AuthResult(access.Token, access.ExpiresAtUtc, refreshToken);
    }
}
