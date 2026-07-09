using System.Text.RegularExpressions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.ExternalServices;

/// <summary>
/// Shared email HTML helpers: wraps a body fragment in a responsive shell (with a brand-name header)
/// and derives a plain-text alternative from HTML. Centralising both transforms keeps the
/// BrandedEmailService decorator and the two providers consistent. The header shows the brand's
/// display name — the tenant's business for customer mail, "ROCloud" for owner/platform mail (Phase 2a;
/// logo/colour is a later step).
/// </summary>
public static partial class EmailHtml
{
    // Table-based, inline-styled layout — the only combination email clients render reliably.
    private const string Shell = """
        <!doctype html>
        <html>
          <body style="margin:0;padding:0;background-color:#f4f5f7;">
            <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background-color:#f4f5f7;">
              <tr>
                <td align="center" style="padding:24px 12px;">
                  <table role="presentation" cellpadding="0" cellspacing="0" width="600" style="max-width:600px;width:100%;background-color:#ffffff;border-radius:8px;border:1px solid #e6e8eb;">
                    <tr>
                      <td style="padding:20px 32px;border-bottom:1px solid #eef0f2;font-family:Arial,Helvetica,sans-serif;font-size:18px;font-weight:bold;color:#1f2733;">
                        {{brandName}}
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:28px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.6;color:#1f2733;">
                        {{content}}
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:16px 32px;border-top:1px solid #eef0f2;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:1.5;color:#8a94a6;">
                        This is an automated message — please do not reply to this email.
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
          </body>
        </html>
        """;

    /// <summary>Wrap a body fragment in the shell with the ROCloud brand (owner/platform mail).</summary>
    public static string Wrap(string bodyFragment)
        => Wrap(bodyFragment, EmailBrand.RoCloud);

    /// <summary>Wrap a body fragment in the shell under the given brand, turning newlines into line
    /// breaks so plain-text bodies keep their paragraphs (previously flattened in the HTML slot).</summary>
    public static string Wrap(string bodyFragment, EmailBrand brand)
        => Shell
            .Replace("{{brandName}}", System.Net.WebUtility.HtmlEncode((brand ?? EmailBrand.RoCloud).DisplayName))
            .Replace("{{content}}", Nl2Br(bodyFragment ?? string.Empty));

    /// <summary>A plain-text rendering of an HTML email, for the multipart text alternative.</summary>
    public static string ToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var t = AnchorPattern().Replace(html, "$2 ($1)");   // <a href="X">Y</a> -> "Y (X)"
        t = BreakPattern().Replace(t, "\n");                // <br>, </p>, </div>, </li> -> newline
        t = TagPattern().Replace(t, string.Empty);          // strip remaining tags
        t = System.Net.WebUtility.HtmlDecode(t);
        t = LineTrimPattern().Replace(t, "\n");             // drop indentation left by the HTML shell
        t = BlankLinePattern().Replace(t, "\n\n");          // collapse runs of blank lines
        return t.Trim();
    }

    private static string Nl2Br(string s)
        => s.Replace("\r\n", "\n").Replace("\n", "<br>\n");

    [GeneratedRegex("""<a\b[^>]*?href=["']([^"']*)["'][^>]*>(.*?)</a>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorPattern();

    [GeneratedRegex("""<br\s*/?>|</p>|</div>|</li>""", RegexOptions.IgnoreCase)]
    private static partial Regex BreakPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"[ \t]*\n[ \t]*")]
    private static partial Regex LineTrimPattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinePattern();
}
