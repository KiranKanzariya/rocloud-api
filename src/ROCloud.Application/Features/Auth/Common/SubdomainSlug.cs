using System.Text;

namespace ROCloud.Application.Features.Auth.Common;

/// <summary>
/// Derives a tenant subdomain slug from free text (business name or typed subdomain): lowercase,
/// ASCII letters/digits, single hyphens between words, trimmed. Shared by registration (password +
/// Google) and the live availability check so they all slugify identically.
/// </summary>
public static class SubdomainSlug
{
    public static string From(string input)
    {
        var sb = new StringBuilder(input.Length);
        var lastDash = false;
        foreach (var ch in input.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash && sb.Length > 0)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        return sb.ToString().Trim('-');
    }
}
