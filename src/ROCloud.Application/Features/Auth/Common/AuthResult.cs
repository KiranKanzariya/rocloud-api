namespace ROCloud.Application.Features.Auth.Common;

/// <summary>
/// Internal auth result returned by command handlers. The controller puts
/// <see cref="RefreshToken"/> in an HttpOnly cookie and returns only the access
/// token + expiry in the response body (guide §10.6).
/// </summary>
public sealed record AuthResult(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken);
