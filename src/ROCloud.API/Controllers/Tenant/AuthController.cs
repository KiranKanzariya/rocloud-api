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
using ROCloud.Application.Features.Auth.Queries.GetSessions;
using ROCloud.Application.Features.Auth.Commands.RevokeSession;
using RefreshCmd = ROCloud.Application.Features.Auth.Commands.RefreshToken.RefreshTokenCommand;

namespace ROCloud.API.Controllers.Tenant;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// Current refresh-cookie name prefix, suffixed with the workspace — <c>roc_rt_aqua</c>.
    /// </summary>
    /// <remarks>
    /// Renamed from <c>refresh_token</c> when the path widened (see <see cref="RefreshPath"/>). A cookie
    /// is keyed by host + PATH + name, so keeping the old name at a new path would have left the browser
    /// holding two cookies of the same name and sending both — and it sends the more specific path
    /// first, which is the older, already-rotated token. That would look exactly like a replayed token,
    /// and the replay check would revoke the device. A new name makes the two eras unambiguous.
    /// </remarks>
    private const string RefreshCookie = "roc_rt";

    /// <summary>
    /// Widened from <c>/api/auth/refresh</c> so the cookie also reaches <c>/api/auth/logout</c>.
    /// </summary>
    /// <remarks>
    /// It previously did not, which meant a web sign-out never revoked its <c>user_sessions</c> row —
    /// the handler received no token and returned early, leaving a live 30-day credential in the
    /// database for a session the user believed they had ended. Everything under <c>/api/auth</c> is
    /// this controller, so the widening exposes the cookie to no code outside it.
    /// </remarks>
    private const string RefreshPath = "/api/auth";

    // ── TRANSITIONAL: the two older cookie shapes, read but never written. Remove them together with
    //    AdoptLegacySessionAsync once every session issued before this deploy has expired
    //    (Jwt:RefreshTokenExpiryDays). Until then, dropping them signs out everyone already on the web.
    private const string LegacyRefreshCookie = "refresh_token";
    private const string LegacyRefreshPath = "/api/auth/refresh";

    /// <summary>
    /// Opt-in marker for clients that have no cookie jar. Sent as <c>X-Client: mobile</c> by the
    /// Flutter app; browsers never send it, so the web keeps the HttpOnly cookie unchanged.
    /// </summary>
    private const string ClientHeader = "X-Client";
    private const string NativeClient = "mobile";

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
    /// <remarks>
    /// The workspace is passed so the grant can be checked against the host it is being redeemed on.
    /// A grant minted for one workspace and posted to another used to be honoured, filing that
    /// workspace's session under the other's cookie name — after which every request 403'd on
    /// TENANT_MISMATCH and every refresh 401'd on the workspace guard. A wedged tab, for a mistake the
    /// server can see and refuse.
    /// </remarks>
    [HttpPost("google-handoff")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleHandoff([FromBody] GoogleHandoffRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new GoogleHandoffCommand(body.Grant, ResolveSubdomain()), ct);
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
        var subdomain = ResolveSubdomain();

        var token = ReadRefreshCookie(subdomain);
        if (string.IsNullOrEmpty(token))
            token = body?.RefreshToken;

        if (string.IsNullOrEmpty(token))
            return Unauthorized(new { error = "No refresh token.", code = "NO_REFRESH_TOKEN" });

        // The workspace this request was made on. Passed so a session cannot be restored onto a
        // different tenant's subdomain — see RefreshTokenCommandHandler.
        var result = await _mediator.Send(new RefreshCmd(token, subdomain), ct);
        return AuthOk(result);
    }

    /// <summary>
    /// Signs out THIS device. The refresh token says which one: a browser sends it as the cookie, a
    /// native client posts it in the body. The access token is blocklisted too when one is presented.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deliberately anonymous.</b> It used to require <c>[Authorize]</c>, which meant that leaving a
    /// tab idle past the access token's hour and then clicking Sign out produced a 401 in the
    /// authorization filter — before the action body, and therefore before
    /// <see cref="ClearRefreshCookie"/>. The portal swallowed that error and cleared only its in-memory
    /// state, so the UI showed the user signed out while the refresh cookie sat untouched in the jar;
    /// the next page load restored the session from it. Sign out has to work when the access token has
    /// expired, because that is exactly when people reach for it.
    /// </para>
    /// <para>
    /// Being anonymous costs nothing: the refresh token IS the credential, and revoking a session
    /// needs no more authority than holding its token. An attacker who has one can already use it.
    /// </para>
    /// What must never happen is signing out of one device ending every session on the account, which
    /// is what the old single-token column did.
    /// </remarks>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshRequest? body,
        CancellationToken ct)
    {
        var token = ReadRefreshCookie(ResolveSubdomain());
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

    /// <summary>The caller's own signed-in devices, newest activity first, with the current one flagged.</summary>
    [HttpGet("sessions")]
    [Authorize]
    public async Task<IActionResult> Sessions(CancellationToken ct)
        => Ok(new { sessions = await _mediator.Send(new GetSessionsQuery(), ct) });

    /// <summary>
    /// Ends one of the caller's own devices remotely — the only way to sign out a phone you no longer
    /// have, or a clone of the app you did not make.
    /// </summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    [Authorize]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct)
    {
        await _mediator.Send(new RevokeSessionCommand(sessionId), ct);

        // Revoking the device you are currently on is a legitimate way to sign out, so clear the
        // cookie too — otherwise the browser would keep a token whose chain is already dead and the
        // next page load would bounce through a failed refresh to the sign-in page.
        if (sessionId == _currentSessionId()) ClearRefreshCookie();

        return Ok(new { success = true });

        Guid? _currentSessionId() =>
            Guid.TryParse(User.FindFirst("sid")?.Value, out var id) ? id : null;
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
        if (IsNativeClient() && !HasRefreshCookie())
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

    /// <summary>
    /// Whether this request arrived carrying a refresh cookie — i.e. it came from a cookie jar.
    /// </summary>
    /// <remarks>
    /// This is what makes the carrier follow the CREDENTIAL rather than a header the caller chooses.
    /// <c>X-Client: mobile</c> alone used to decide it, and a header is not a client: script running on
    /// the portal could add it to a <c>fetch(..., { credentials: 'include' })</c> against
    /// <c>/api/auth/refresh</c>, and the API would read the HttpOnly cookie, rotate it, and hand the
    /// new refresh token back in JSON where the script could read it. That turned any XSS from
    /// "acts as the user while the tab is open" into "holds a 30-day credential", which is precisely
    /// what HttpOnly exists to prevent.
    /// <para>
    /// A genuine native client has no cookie jar and never trips this, so it is unaffected.
    /// </para>
    /// </remarks>
    private bool HasRefreshCookie() =>
        !string.IsNullOrEmpty(ReadRefreshCookie(ResolveSubdomain()));

    /// <summary>
    /// The refresh cookie's name for the workspace being acted on — <c>refresh_token_aqua</c>.
    /// </summary>
    /// <remarks>
    /// One cookie per workspace, because a cookie is keyed by host + path + NAME and this one is
    /// set by the API host, not by a tenant's subdomain. Under a single name, signing in to a
    /// second workspace overwrote the first workspace's cookie: two tabs, and whichever was signed
    /// into last owned the only session the browser could carry. Reloading the other tab found
    /// somebody else's token and signed that tab out.
    /// <para>
    /// The database has held many sessions per user since user_sessions; this is the browser
    /// catching up with it. Subdomains are lowercase ASCII and hyphens, which is already a valid
    /// cookie name, so nothing has to be escaped.
    /// </para>
    /// </remarks>
    private static string ScopedName(string prefix, string? subdomain) =>
        string.IsNullOrWhiteSpace(subdomain) ? prefix : $"{prefix}_{subdomain}";

    private string RefreshCookieFor(string? subdomain) => ScopedName(RefreshCookie, subdomain);

    private CookieOptions CookieOptionsFor(string path, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Expires = expires,
        Path = path
    };

    private void SetRefreshCookie(string refreshToken)
    {
        var subdomain = ResolveSubdomain();

        Response.Cookies.Append(
            RefreshCookieFor(subdomain),
            refreshToken,
            CookieOptionsFor(RefreshPath, DateTimeOffset.UtcNow.AddDays(_refreshCookieDays)));

        // Retire any pre-rename cookie in the same breath. Left alone it would keep being sent
        // alongside the new one until it expired, and every read would have to guess which of two
        // tokens for the same session was current.
        ExpireLegacyCookies(subdomain);
    }

    /// <summary>
    /// Reads this workspace's refresh cookie, newest shape first.
    /// </summary>
    /// <remarks>
    /// TRANSITIONAL — the two fallbacks cover sessions issued before this deploy: the per-workspace
    /// <c>refresh_token_aqua</c> at the old narrow path, and before that a single unnamed
    /// <c>refresh_token</c> for the whole browser. Without them the deploy signs out everyone already
    /// on the web. Remove once those have expired (Jwt:RefreshTokenExpiryDays), together with
    /// <c>AdoptLegacySessionAsync</c>.
    /// </remarks>
    private string? ReadRefreshCookie(string? subdomain)
    {
        foreach (var name in new[]
                 {
                     RefreshCookieFor(subdomain),
                     ScopedName(LegacyRefreshCookie, subdomain),
                     LegacyRefreshCookie
                 })
        {
            var value = Request.Cookies[name];
            if (!string.IsNullOrEmpty(value)) return value;
        }

        return null;
    }

    /// <summary>
    /// Expires every shape of the refresh cookie, at every path any of them was ever written to.
    /// </summary>
    /// <remarks>
    /// A cookie is keyed by host + path + name, so clearing one shape leaves the others in the jar and
    /// a later read finds a session the user thought they had ended. Being exhaustive here is cheap;
    /// missing one is a sign-out that does not sign out.
    /// </remarks>
    private void ClearRefreshCookie()
    {
        var subdomain = ResolveSubdomain();
        var gone = DateTimeOffset.UtcNow.AddDays(-1);

        Response.Cookies.Append(RefreshCookieFor(subdomain), string.Empty, CookieOptionsFor(RefreshPath, gone));
        ExpireLegacyCookies(subdomain);
    }

    private void ExpireLegacyCookies(string? subdomain)
    {
        var gone = DateTimeOffset.UtcNow.AddDays(-1);
        foreach (var name in new[] { ScopedName(LegacyRefreshCookie, subdomain), LegacyRefreshCookie })
        foreach (var path in new[] { LegacyRefreshPath, RefreshPath })
            Response.Cookies.Append(name, string.Empty, CookieOptionsFor(path, gone));
    }

    private string? ResolveSubdomain()
    {
        var header = Request.Headers["X-Tenant"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header)) return header;

        // ReservedSubdomains is the single definition, shared with TenantMiddleware and enforced at
        // registration. It previously omitted "app" here and "rocloud" everywhere, so the portal's own
        // bare-domain host and the apex label could each have been read as a workspace name.
        var label = Request.Host.Host.Split('.').FirstOrDefault();
        return !string.IsNullOrWhiteSpace(label) && !ReservedSubdomains.IsPlatformHostLabel(label)
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
