using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.Identity;

/// <summary>Verifies Google ID tokens via Google.Apis.Auth (guide §5).</summary>
/// <remarks>
/// Two rules here are load-bearing, and both used to be missing:
/// <list type="number">
/// <item><b>The audience is mandatory.</b> Google's validator skips the <c>aud</c> check entirely when
/// <c>ValidationSettings.Audience</c> is null, so a blank config used to mean "accept an ID token minted
/// for ANY OAuth client on earth". Since the token is still a genuine, correctly signed Google token,
/// nothing else caught it: anyone holding a token for a victim's Google account — from their own app,
/// or any app the victim had signed into — could present it here and be let in as that user. This class
/// now refuses to validate at all rather than validate loosely.</item>
/// <item><b>The email must be verified.</b> Both sign-in handlers fall back to matching on email when
/// no <c>GoogleId</c> is linked yet, so an unverified address is a way to claim someone else's account.
/// Rejected here rather than in each handler, so a new caller cannot forget it.</item>
/// </list>
/// </remarks>
public class GoogleAuthService : IGoogleAuthService
{
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(IConfiguration config, ILogger<GoogleAuthService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        var audiences = AllowedAudiences();
        if (audiences.Length == 0)
        {
            // Fail CLOSED. Google sign-in being unavailable is a visible, reported outage that gets
            // fixed; accepting any issuer's token is a silent one that does not.
            _logger.LogError(
                "Google sign-in is not configured: set Google__ClientIds (or Google__ClientId). "
                + "Refusing to validate rather than accepting tokens from any OAuth client.");
            return null;
        }

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings { Audience = audiences };
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            if (!payload.EmailVerified)
            {
                _logger.LogWarning(
                    "Google ID token rejected: email_verified is false for subject {Subject}.", payload.Subject);
                return null;
            }

            if (string.IsNullOrWhiteSpace(payload.Email))
            {
                _logger.LogWarning(
                    "Google ID token rejected: no email claim for subject {Subject}.", payload.Subject);
                return null;
            }

            return new GoogleUserInfo(payload.Subject, payload.Email, payload.Name ?? payload.Email, payload.Picture);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning("Google ID token validation failed: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Every client ID allowed to mint a token for us. A list rather than one value because each
    /// platform gets its own OAuth client and therefore its own <c>aud</c>: the portal's web client,
    /// the Android client, and any future iOS one. The Android app requests an ID token with the WEB
    /// client as its <c>serverClientId</c>, so today both entries are usually the same value — but the
    /// shape has to allow more than one or adding a platform silently locks it out.
    /// <para><c>Google:ClientId</c> is still honoured so existing deployments keep working.</para>
    /// </summary>
    private string[] AllowedAudiences()
    {
        var list = _config.GetSection("Google:ClientIds").Get<string[]>() ?? [];
        var single = _config["Google:ClientId"];
        if (!string.IsNullOrWhiteSpace(single))
            list = [.. list, single];

        return [.. list.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct()];
    }
}
