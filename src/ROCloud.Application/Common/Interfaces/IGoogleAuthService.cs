namespace ROCloud.Application.Common.Interfaces;

/// <summary>Validated identity returned by Google after verifying an ID token.</summary>
public sealed record GoogleUserInfo(string Subject, string Email, string Name, string? Picture);

/// <summary>Verifies Google ID tokens (guide §5 — Google OAuth flow).</summary>
public interface IGoogleAuthService
{
    /// <summary>Validates the ID token's signature/audience and returns the user info, or null if invalid.</summary>
    Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken ct = default);
}
