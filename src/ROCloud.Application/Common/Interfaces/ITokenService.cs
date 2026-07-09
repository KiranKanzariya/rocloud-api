using ROCloud.Application.Common.Models;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Common.Interfaces;

/// <summary>JWT access-token generation and refresh-token handling (guide §5, §10.3).</summary>
public interface ITokenService
{
    /// <summary>Issues a signed JWT (60 min) with all ROCloud claims, including a unique jti.</summary>
    GeneratedAccessToken GenerateAccessToken(User user, Tenant tenant, IReadOnlyCollection<string> permissions);

    /// <summary>
    /// Issues a signed JWT for a platform staff member (super-admin portal, guide §26): claims
    /// sub/jti/email/name/platform_role, with NO tenant_id. Same signing key as tenant tokens.
    /// </summary>
    GeneratedAccessToken GeneratePlatformToken(PlatformUser platformUser);

    /// <summary>Cryptographically random 64-byte refresh token (base64).</summary>
    string GenerateRefreshToken();

    /// <summary>SHA-256 hash of a refresh token — what is stored/compared in the DB.</summary>
    string HashRefreshToken(string refreshToken);

    /// <summary>
    /// Mints a short-lived (≈90s) single-purpose JWT that lets a verified Google identity establish a
    /// session on a tenant subdomain (apex → subdomain handoff, guide §5). Carries only userId/tenantId.
    /// </summary>
    string GenerateHandoffToken(Guid userId, Guid tenantId);

    /// <summary>Validates a handoff token (signature, lifetime, purpose) and returns its ids, or null.</summary>
    HandoffPayload? ValidateHandoffToken(string token);
}

/// <summary>The (user, tenant) a verified Google handoff token authorises.</summary>
public sealed record HandoffPayload(Guid UserId, Guid TenantId);
