using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ROCloud.Application.Features.Platform.Auth.Commands.BootstrapAdmin;
using ROCloud.Application.Features.Platform.Auth.Commands.PlatformForgotPassword;
using ROCloud.Application.Features.Platform.Auth.Commands.PlatformLogin;
using ROCloud.Application.Features.Platform.Auth.Commands.PlatformLogout;
using ROCloud.Application.Features.Platform.Auth.Commands.PlatformResetPassword;
using ROCloud.Application.Features.Platform.Auth.Common;
using PlatformRefreshCmd = ROCloud.Application.Features.Platform.Auth.Commands.PlatformRefreshToken.PlatformRefreshTokenCommand;

namespace ROCloud.API.Controllers.Platform;

/// <summary>
/// Super-admin portal authentication (guide §26). Authenticates against platform_users and issues
/// platform JWTs (platform_role claim, no tenant). Refresh token stored in an HttpOnly cookie.
/// </summary>
[ApiController]
[Route("api/platform/auth")]
public class PlatformAuthController : ControllerBase
{
    private const string RefreshCookie = "platform_refresh_token";
    private const string RefreshPath = "/api/platform/auth/refresh";

    private readonly IMediator _mediator;
    private readonly int _refreshCookieDays;

    public PlatformAuthController(IMediator mediator, IConfiguration config)
    {
        _mediator = mediator;
        _refreshCookieDays = int.TryParse(config["Jwt:RefreshTokenExpiryDays"], out var d) ? d : 30;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] PlatformLoginCommand command, CancellationToken ct)
        => AuthOk(await _mediator.Send(command, ct));

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var token = Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(token))
            return Unauthorized(new { error = "No refresh token.", code = "NO_REFRESH_TOKEN" });

        return AuthOk(await _mediator.Send(new PlatformRefreshCmd(token), ct));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Guid.TryParse(User.FindFirst("sub")?.Value, out var id))
            await _mediator.Send(new PlatformLogoutCommand(id), ct);
        ClearRefreshCookie();
        return Ok(new { success = true });
    }

    /// <summary>Emails a reset token to a platform admin who forgot their password.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] PlatformForgotPasswordCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        return Ok(new { success = true });   // always ok — never reveal whether the email exists
    }

    /// <summary>Completes a platform password reset with the emailed token.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] PlatformResetPasswordCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        return Ok(new { success = true });
    }

    /// <summary>First-run bootstrap: creates the initial SuperAdmin when none exist yet.</summary>
    [HttpPost("bootstrap")]
    [AllowAnonymous]
    public async Task<IActionResult> Bootstrap([FromBody] BootstrapAdminCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Ok(new { id });
    }

    // ─── helpers ──────────────────────────────────────────────────────────
    private IActionResult AuthOk(PlatformAuthResult result)
    {
        SetRefreshCookie(result.RefreshToken);
        return Ok(new { accessToken = result.AccessToken, expiresAt = result.ExpiresAtUtc });
    }

    private void SetRefreshCookie(string refreshToken) =>
        Response.Cookies.Append(RefreshCookie, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(_refreshCookieDays),
            Path = RefreshPath
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Append(RefreshCookie, string.Empty, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(-1),
            Path = RefreshPath
        });
}
