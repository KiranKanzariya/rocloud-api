namespace ROCloud.Application.Common.Interfaces;

/// <summary>Exposes the authenticated user's identity/claims for the current request.</summary>
/// <remarks>
/// The optional claims below carry default implementations returning null. They are additive request
/// metadata that most callers never read, and the alternative — every stand-in across the test suite
/// having to declare four properties it does not use — makes those tests noisier without making
/// anything safer. The real implementation overrides all of them.
/// </remarks>
public interface ICurrentUserService
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? TenantId { get; }

    /// <summary>The signed-in address (the <c>email</c> claim), for attributing actions to a person.</summary>
    string? Email => null;

    /// <summary>JWT id (jti) — used for token revocation on logout.</summary>
    string? Jti { get; }

    /// <summary>
    /// The signed-in device this token belongs to (the <c>sid</c> claim). Null on an impersonation
    /// token, which has no session row, and on tokens minted before the claim existed.
    /// </summary>
    Guid? SessionId => null;

    /// <summary>Absolute UTC expiry of the current access token (from the exp claim).</summary>
    DateTime? AccessTokenExpiresAt { get; }

    /// <summary>
    /// When the current access token was issued (our <c>token_iat</c> claim), for checking it against
    /// an account-wide revocation. Null on a token minted before this claim existed.
    /// </summary>
    DateTime? AccessTokenIssuedAt => null;

    /// <summary>
    /// The platform operator driving an impersonation session (the <c>act</c> claim), or null for an
    /// ordinary sign-in. Present so audit entries can say who really performed an action.
    /// </summary>
    string? ActingAs => null;

    IReadOnlyCollection<string> Permissions { get; }
}
