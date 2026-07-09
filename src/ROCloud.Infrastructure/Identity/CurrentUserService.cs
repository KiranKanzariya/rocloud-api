using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.Identity;

/// <summary>Reads the authenticated user's claims from the current HTTP context.</summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? User => _http.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId => Guid.TryParse(User?.FindFirst("sub")?.Value, out var id) ? id : null;

    public Guid? TenantId => Guid.TryParse(User?.FindFirst("tenant_id")?.Value, out var id) ? id : null;

    public string? Jti => User?.FindFirst("jti")?.Value;

    public DateTime? AccessTokenExpiresAt =>
        long.TryParse(User?.FindFirst("exp")?.Value, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime
            : null;

    public IReadOnlyCollection<string> Permissions =>
        User?.FindFirst("permissions")?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
}
