namespace ROCloud.Application.Features.Auth.Common;

/// <summary>
/// Subdomains no tenant may hold, because the platform already answers on them.
/// </summary>
/// <remarks>
/// <para>
/// There was no such list. Registration accepted any <c>^[a-z0-9-]{3,100}$</c>, so a registrant could
/// claim <c>app</c> — which is the owner portal's own bare-domain host — or <c>api</c>, <c>admin</c>,
/// <c>www</c>. That is not a data leak (the tenant middleware still keys off the JWT claim), but the
/// tenant's portal URL, their password-reset links and their Google handoff URLs would all have
/// pointed at a shared platform host. A workspace whose sign-in page is the same address the platform
/// uses is a phishing surface handed out at signup.
/// </para>
/// <para>
/// This is also the ONE definition. Two partial lists existed — <c>AuthController</c> and
/// <c>TenantMiddleware</c> each had <c>localhost, api, admin, www</c>, the portal had those plus
/// <c>app</c>, and none had <c>rocloud</c>, the apex label. They are used for different questions
/// ("is this host label a workspace?" versus "may someone register this?") but they must not disagree,
/// so both now read from here.
/// </para>
/// </remarks>
public static class ReservedSubdomains
{
    /// <summary>
    /// Host labels that never name a tenant. Used when resolving a workspace from the request host.
    /// </summary>
    public static readonly string[] HostLabels =
    [
        "localhost",
        "api",        // the API itself
        "admin",      // super-admin portal
        "app",        // owner portal on the bare domain
        "www",
        "rocloud"     // the apex label of rocloud.in — Host.Split('.')[0] yields it
    ];

    /// <summary>
    /// Everything <see cref="HostLabels"/> covers, plus names that are not hosts today but would be
    /// confusing, impersonating, or awkward to reclaim once someone owned them.
    /// </summary>
    private static readonly string[] Extra =
    [
        // Infrastructure names likely to be wanted later. Cheap to reserve now, impossible to take
        // back from a paying tenant afterwards.
        "assets", "cdn", "static", "files", "img", "images", "media",
        "mail", "smtp", "email", "webmail", "mx", "ns", "ns1", "ns2", "dns",
        "staging", "stage", "dev", "test", "demo", "sandbox", "preview", "beta", "alpha",
        "status", "health", "monitor", "metrics", "grafana", "logs",
        "docs", "help", "support", "blog", "news", "about", "careers", "shop", "store",
        "portal", "dashboard", "console", "account", "accounts", "billing", "pay", "payments",
        "auth", "login", "signin", "signup", "register", "sso", "oauth", "identity",
        // Names that let a tenant pass themselves off as us.
        "rocloud", "ro-cloud", "official", "security", "abuse", "postmaster", "hostmaster",
        "root", "system", "internal", "private", "public"
    ];

    private static readonly HashSet<string> All =
        new(HostLabels.Concat(Extra), StringComparer.OrdinalIgnoreCase);

    /// <summary>True when this slug may not be registered by a tenant.</summary>
    public static bool IsReserved(string? subdomain) =>
        !string.IsNullOrWhiteSpace(subdomain) && All.Contains(subdomain.Trim());

    /// <summary>True when this host label never names a workspace.</summary>
    public static bool IsPlatformHostLabel(string? label) =>
        !string.IsNullOrWhiteSpace(label)
        && HostLabels.Contains(label.Trim(), StringComparer.OrdinalIgnoreCase);
}
