using Microsoft.AspNetCore.Localization;

namespace ROCloud.API.Localization;

/// <summary>
/// Resolves request culture from the explicit <c>X-Language</c> header sent by the
/// portals (guide §4c.4). Registered first so it takes priority over the built-in
/// Accept-Language provider.
/// </summary>
public class CustomHeaderRequestCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var lang = httpContext.Request.Headers["X-Language"].FirstOrDefault();
        return Task.FromResult(string.IsNullOrEmpty(lang)
            ? null
            : new ProviderCultureResult(lang));
    }
}
