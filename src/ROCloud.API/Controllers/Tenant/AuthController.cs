using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Configuration;
using ROCloud.Application.Features.Auth.Commands.FindWorkspace;
using ROCloud.Application.Features.Auth.Commands.ForgotPassword;
using ROCloud.Application.Features.Auth.Commands.GoogleHandoff;
using ROCloud.Application.Features.Auth.Commands.GoogleLogin;
using ROCloud.Application.Features.Auth.Commands.GoogleWorkspaces;
using ROCloud.Application.Features.Auth.Commands.Login;
using ROCloud.Application.Features.Auth.Commands.Logout;
using ROCloud.Application.Features.Auth.Commands.Register;
using ROCloud.Application.Features.Auth.Commands.RegisterGoogle;
using ROCloud.Application.Features.Auth.Commands.ResetPassword;
using ROCloud.Application.Features.Auth.Common;
using ROCloud.Application.Features.Auth.Queries.CheckSubdomain;
using RefreshCmd = ROCloud.Application.Features.Auth.Commands.RefreshToken.RefreshTokenCommand;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const string RefreshCookie = "refresh_token";
    private const string RefreshPath = "/api/auth/refresh";

    /// <summary>
    /// Opt-in marker for clients that have no cookie jar. Sent as <c>X-Client: mobile</c> by the
    /// Flutter app; browsers never send it, so the web keeps the HttpOnly cookie unchanged.
    /// </summary>
    private const string ClientHeader = "X-Client";
    private const string NativeClient = "mobile";

    private static readonly string[] ReservedHostLabels = { "localhost", "api", "admin", "www" };

    private readonly IMediator _mediator;
    private readonly int _refreshCookieDays;

    public AuthController(IMediator mediator, IConfiguration config)
    {
        _mediator = mediator;
        _refreshCookieDays = int.TryParse(config["Jwt:RefreshTokenExpiryDays"], out var d) ? d : 30;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new LoginCommand(body.Email, body.Password, ResolveSubdomain()), ct);
        return AuthOk(result);
    }

    [HttpPost("google")]
    [AllowAnonymous]
    public async Task<IActionResult> Google([FromBody] GoogleLoginRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new GoogleLoginCommand(body.IdToken, ResolveSubdomain()), ct);
        return AuthOk(result);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new RegisterCommand(
            body.BusinessName, body.OwnerName, body.Email, body.Password, body.Mobile, body.PlanType, body.Subdomain), ct);
        return AuthOk(result);
    }

    [HttpPost("register-google")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterGoogle([FromBody] RegisterGoogleRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new RegisterGoogleCommand(
            body.IdToken, body.BusinessName, body.Mobile, body.PlanType, body.Subdomain), ct);
        return AuthOk(result);
    }

    /// <summary>
    /// Apex Google sign-in: verify the Google id-token and return the workspaces it can enter, each with
    /// a one-time handoff URL. Runs on the central app domain (a single Google Authorized origin) so we
    /// don't have to register every tenant subdomain with Google.
    /// </summary>
    [HttpPost("google-resolve")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleResolve([FromBody] GoogleResolveRequest body, CancellationToken ct)
    {
        var workspaces = await _mediator.Send(new ResolveGoogleWorkspacesCommand(body.IdToken), ct);
        return Ok(new { workspaces });
    }

    /// <summary>Subdomain handoff: exchange a one-time grant token for a real session on this tenant.</summary>
    [HttpPost("google-handoff")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleHandoff([FromBody] GoogleHandoffRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new GoogleHandoffCommand(body.Grant), ct);
        return AuthOk(result);
    }

    /// <summary>
    /// Rotates the refresh token. The web sends it as an HttpOnly cookie; a native client has no
    /// cookie jar, so it may post the token in the body instead. The cookie is preferred when both
    /// are present — a browser's own cookie should never be overridable by request content.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshRequest? body,
        CancellationToken ct)
    {
        var token = Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(token))
            token = body?.RefreshToken;

        if (string.IsNullOrEmpty(token))
            return Unauthorized(new { error = "No refresh token.", code = "NO_REFRESH_TOKEN" });

        // The workspace this request was made on. Passed so a session cannot be restored onto a
        // different tenant's subdomain — see RefreshTokenCommandHandler.
        var result = await _mediator.Send(new RefreshCmd(token, ResolveSubdomain()), ct);
        return AuthOk(result);
    }

    /// <summary>
    /// Signs out THIS device. The refresh token says which one, and a native client posts it in the
    /// body.
    /// <para>
    /// A browser does not: the refresh cookie is path-scoped to <c>/api/auth/refresh</c>, so it is
    /// not sent here. That is fine and not worth widening the cookie's path for — the response
    /// clears the cookie, which leaves the session row holding a token no one can present any more,
    /// and the pruning in <c>AuthTokenIssuer</c> collects it later.
    /// </para>
    /// Either way the access token is blocklisted immediately. What must never happen is signing out
    /// of one device ending every session on the account, which is what the old single-token column
    /// did.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshRequest? body,
        CancellationToken ct)
    {
        var token = Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(token))
            token = body?.RefreshToken;

        await _mediator.Send(new LogoutCommand(token), ct);
        ClearRefreshCookie();
        return Ok(new { success = true });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest body, CancellationToken ct)
    {
        await _mediator.Send(new ForgotPasswordCommand(body.Email, ResolveSubdomain()), ct);
        return Ok(new { message = "If an account exists, a reset link has been sent." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest body, CancellationToken ct)
    {
        await _mediator.Send(new ResetPasswordCommand(body.Token, body.NewPassword), ct);
        return Ok(new { message = "Password has been reset. Please sign in." });
    }

    /// <summary>Live check for the registration subdomain field — returns the slug + whether it's free.</summary>
    [HttpGet("subdomain-available")]
    [AllowAnonymous]
    public async Task<IActionResult> SubdomainAvailable([FromQuery] string? value, CancellationToken ct)
        => Ok(await _mediator.Send(new CheckSubdomainQuery(value ?? string.Empty), ct));

    /// <summary>"Forgot your workspace?" — emails the caller their tenant portal URL(s). Anti-enumeration.</summary>
    [HttpPost("find-workspace")]
    [AllowAnonymous]
    public async Task<IActionResult> FindWorkspace([FromBody] FindWorkspaceRequest body, CancellationToken ct)
    {
        await _mediator.Send(new FindWorkspaceCommand(body.Email), ct);
        return Ok(new { message = "If an account exists, we've emailed your sign-in link." });
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// The single funnel every token-issuing endpoint returns through.
    /// <para>
    /// Web clients keep the existing behaviour exactly: refresh token in an HttpOnly, Secure,
    /// SameSite=Strict cookie scoped to the refresh path, and never in the body — that is what
    /// keeps XSS on the portal from being able to read it.
    /// </para>
    /// <para>
    /// A native client (<c>X-Client: mobile</c>) cannot read a cookie at all, so it gets the token
    /// in the body and no cookie is set. The trade is real but unavoidable for a native app; the
    /// token lands in the Android Keystore, which no other app on the device can read.
    /// </para>
    /// </summary>
    private IActionResult AuthOk(AuthResult result)
    {
        if (IsNativeClient())
        {
            return Ok(new
            {
                accessToken = result.AccessToken,
                expiresAt = result.ExpiresAtUtc,
                refreshToken = result.RefreshToken
            });
        }

        SetRefreshCookie(result.RefreshToken);
        return Ok(new { accessToken = result.AccessToken, expiresAt = result.ExpiresAtUtc });
    }

    private bool IsNativeClient() =>
        string.Equals(
            Request.Headers[ClientHeader].FirstOrDefault(),
            NativeClient,
            StringComparison.OrdinalIgnoreCase);

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

    private string? ResolveSubdomain()
    {
        var header = Request.Headers["X-Tenant"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header)) return header;

        var label = Request.Host.Host.Split('.').FirstOrDefault();
        return !string.IsNullOrWhiteSpace(label)
               && !ReservedHostLabels.Contains(label, StringComparer.OrdinalIgnoreCase)
            ? label
            : null;
    }
}

// ─── request bodies ───────────────────────────────────────────────────────
public sealed record LoginRequest(string Email, string Password);
public sealed record GoogleLoginRequest(string IdToken);
public sealed record RegisterRequest(
    string BusinessName, string OwnerName, string Email, string Password,
    string Mobile, string PlanType, string? Subdomain);
public sealed record RegisterGoogleRequest(
    string IdToken, string BusinessName, string? Mobile, string PlanType, string? Subdomain);
public sealed record GoogleResolveRequest(string IdToken);
public sealed record GoogleHandoffRequest(string Grant);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record FindWorkspaceRequest(string Email);

/// <summary>Body form of the refresh token, for clients with no cookie jar. Optional.</summary>
public sealed record RefreshRequest(string? RefreshToken);
