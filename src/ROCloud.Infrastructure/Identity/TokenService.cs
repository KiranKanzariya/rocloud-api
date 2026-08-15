using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Models;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Identity;

/// <summary>JWT access tokens + random refresh tokens (guide §5, §10.3).</summary>
public class TokenService : ITokenService
{
    private readonly IConfiguration _config;

    public TokenService(IConfiguration config) => _config = config;

    /// <summary>
    /// Issues a tenant access token.
    /// </summary>
    /// <param name="lifetime">
    /// Overrides <c>Jwt:AccessTokenExpiryMinutes</c>. Used by platform impersonation, which has no
    /// business lasting as long as a real working session.
    /// </param>
    /// <param name="actAs">
    /// Who is really driving, when it is not the user named in <c>sub</c> — the platform operator's
    /// address during impersonation. Emitted as the standard <c>act</c> claim so the audit trail says
    /// "staff X acting as owner Y" rather than just "owner Y", which is what it looked like before.
    /// </param>
    public GeneratedAccessToken GenerateAccessToken(
        User user, Tenant tenant, IReadOnlyCollection<string> permissions,
        TimeSpan? lifetime = null, string? actAs = null, Guid? sessionId = null)
    {
        var secret = _config["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "Jwt:Secret is not configured. In Production, supply it via the Jwt__Secret environment variable.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var minutes = int.TryParse(_config["Jwt:AccessTokenExpiryMinutes"], out var m) ? m : 60;
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.Add(lifetime ?? TimeSpan.FromMinutes(minutes));

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("jti", Guid.NewGuid().ToString()),
            // Issued-at, as our own claim rather than the registered "iat". The JWT handler populates
            // the registered one itself, and adding a second would produce a duplicate whose winner
            // depends on library version — not something an authorization decision should rest on.
            // Read by SessionValidityService to refuse tokens minted before a revocation.
            new(TokenIssuedAtClaim, ToUnixSeconds(issuedAt).ToString(), ClaimValueTypes.Integer64),
            new("email", user.Email ?? string.Empty),
            new("name", user.Name),
            new("tenant_id", tenant.Id.ToString()),
            new("tenant_sub", tenant.Subdomain),
            new("tenant_name", tenant.Name),
            new("role_id", user.RoleId?.ToString() ?? string.Empty),
            new("role_name", user.Role?.Name ?? string.Empty),
            new("plan_type", tenant.Plan?.PlanType.ToString() ?? string.Empty),
            new("permissions", string.Join(",", permissions))
        };

        if (!string.IsNullOrWhiteSpace(actAs))
            claims.Add(new Claim("act", actAs));

        // Which signed-in device this token belongs to, so the sessions list can say "this device".
        // Absent on impersonation, which creates no session row.
        if (sessionId is { } sid)
            claims.Add(new Claim("sid", sid.ToString()));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new GeneratedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    /// <summary>Our own issued-at claim. See the note where it is written.</summary>
    public const string TokenIssuedAtClaim = "token_iat";

    private static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();

    public GeneratedAccessToken GeneratePlatformToken(PlatformUser platformUser)
    {
        var secret = _config["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "Jwt:Secret is not configured. In Production, supply it via the Jwt__Secret environment variable.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var minutes = int.TryParse(_config["Jwt:AccessTokenExpiryMinutes"], out var m) ? m : 60;
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddMinutes(minutes);

        var claims = new List<Claim>
        {
            new("sub", platformUser.Id.ToString()),
            new("jti", Guid.NewGuid().ToString()),
            // Same issued-at claim as a tenant token, read for the same reason — see the note there.
            new(TokenIssuedAtClaim, ToUnixSeconds(issuedAt).ToString(), ClaimValueTypes.Integer64),
            new("email", platformUser.Email),
            new("name", platformUser.Name),
            new("platform_role", platformUser.PlatformRole)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new GeneratedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string HashRefreshToken(string refreshToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private const string HandoffPurpose = "google_handoff";

    public string GenerateHandoffToken(Guid userId, Guid tenantId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(RequireSecret()));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("purpose", HandoffPurpose),
            new("jti", Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(90),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public HandoffPayload? ValidateHandoffToken(string token)
    {
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(RequireSecret()));
            // Keep short claim names ("sub", "tenant_id") as-authored — the default handler remaps
            // "sub" to the long nameidentifier URI, which would make the lookups below fail.
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _config["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = _config["Jwt:Audience"],
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(5)
            }, out _);

            if (principal.FindFirst("purpose")?.Value != HandoffPurpose)
                return null;
            if (Guid.TryParse(principal.FindFirst("sub")?.Value, out var userId)
                && Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId))
                return new HandoffPayload(userId, tenantId);
            return null;
        }
        catch
        {
            return null;
        }
    }

    private string RequireSecret()
    {
        var secret = _config["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "Jwt:Secret is not configured. In Production, supply it via the Jwt__Secret environment variable.");
        return secret;
    }
}
