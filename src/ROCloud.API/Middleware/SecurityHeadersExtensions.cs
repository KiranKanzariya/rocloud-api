using NetEscapades.AspNetCore.SecurityHeaders;

namespace ROCloud.API.Middleware;

/// <summary>
/// Builds the canonical ROCloud security-header policy (CSP, HSTS, nosniff,
/// referrer-policy, permissions-policy) per guide §10.5.
/// </summary>
public static class SecurityHeadersExtensions
{
    /// <param name="apiUrl">The API origin to allow in CSP connect-src (App:ApiUrl).</param>
    /// <param name="hstsMaxAgeSeconds">HSTS max-age (SecurityHeaders:HstsMaxAgeSeconds).</param>
    public static HeaderPolicyCollection BuildRoCloudPolicies(string apiUrl, int hstsMaxAgeSeconds = 31_536_000)
    {
        return new HeaderPolicyCollection()
            .AddDefaultSecurityHeaders()
            .AddContentSecurityPolicy(csp =>
            {
                csp.AddDefaultSrc().Self();
                csp.AddScriptSrc().Self()
                    .From("https://accounts.google.com")
                    .From("https://checkout.razorpay.com");
                csp.AddStyleSrc().Self().UnsafeInline().From("https://fonts.googleapis.com");
                csp.AddFontSrc().Self().From("https://fonts.gstatic.com");
                csp.AddImgSrc().Self().Data().From("https:");
                csp.AddConnectSrc().Self().From(apiUrl);
                csp.AddFrameAncestors().None();
                csp.AddBaseUri().Self();
                csp.AddObjectSrc().None();
            })
            // X-Content-Type-Options: nosniff is already emitted by AddDefaultSecurityHeaders().
            .AddStrictTransportSecurityMaxAgeIncludeSubDomainsAndPreload(maxAgeInSeconds: hstsMaxAgeSeconds)
            .AddReferrerPolicyStrictOriginWhenCrossOrigin()
            .AddPermissionsPolicy(builder =>
            {
                builder.AddCamera().None();
                builder.AddMicrophone().None();
                builder.AddGeolocation().Self();
                builder.AddPayment().Self();
            });
    }
}
