namespace ROCloud.Application.Common.Models;

/// <summary>A generated JWT access token plus its absolute UTC expiry.</summary>
public sealed record GeneratedAccessToken(string Token, DateTime ExpiresAtUtc);
