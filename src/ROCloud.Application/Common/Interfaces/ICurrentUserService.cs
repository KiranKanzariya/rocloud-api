namespace ROCloud.Application.Common.Interfaces;

/// <summary>Exposes the authenticated user's identity/claims for the current request.</summary>
public interface ICurrentUserService
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? TenantId { get; }

    /// <summary>JWT id (jti) — used for token revocation on logout.</summary>
    string? Jti { get; }

    /// <summary>Absolute UTC expiry of the current access token (from the exp claim).</summary>
    DateTime? AccessTokenExpiresAt { get; }

    IReadOnlyCollection<string> Permissions { get; }
}
