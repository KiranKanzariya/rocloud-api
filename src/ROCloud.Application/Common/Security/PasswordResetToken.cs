namespace ROCloud.Application.Common.Security;

/// <summary>Cached payload behind a password-reset token (1-hour TTL in ICacheService).</summary>
public sealed record PasswordResetToken(Guid UserId);
