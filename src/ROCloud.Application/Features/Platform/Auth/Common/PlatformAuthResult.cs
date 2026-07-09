namespace ROCloud.Application.Features.Platform.Auth.Common;

/// <summary>Result of a platform login/refresh: an access token plus a rotated refresh token (guide §26).</summary>
public sealed record PlatformAuthResult(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken);
