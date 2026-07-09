using Ganss.Xss;

namespace ROCloud.Application.Common.Sanitisation;

/// <summary>
/// Email-HTML sanitiser (guide §10.5) for the SuperAdmin default-template editor. Starts from the
/// Ganss defaults — which already strip &lt;script&gt;, on* event handlers and javascript: URIs — and
/// additionally allows the table + inline-style markup that HTML email relies on. Owner-authored
/// templates keep the stricter default sanitiser; this wider one is used only by admins.
/// Thread-safe, registered as a singleton.
/// </summary>
public class EmailHtmlSanitizerService : IEmailHtmlSanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    public EmailHtmlSanitizerService()
    {
        _sanitizer = new HtmlSanitizer();

        foreach (var tag in new[]
        {
            "table", "thead", "tbody", "tfoot", "tr", "td", "th", "col", "colgroup",
            "div", "span", "p", "br", "hr", "a", "img", "center", "font",
            "strong", "b", "em", "i", "u", "small", "blockquote",
            "ul", "ol", "li", "h1", "h2", "h3", "h4", "h5", "h6",
        })
            _sanitizer.AllowedTags.Add(tag);

        foreach (var attr in new[]
        {
            "style", "href", "src", "alt", "title", "target", "role", "dir",
            "width", "height", "align", "valign", "border", "cellpadding", "cellspacing",
            "bgcolor", "color", "colspan", "rowspan",
        })
            _sanitizer.AllowedAttributes.Add(attr);

        _sanitizer.AllowedSchemes.Add("mailto");   // http/https already allowed by default
    }

    public string Sanitize(string html) => string.IsNullOrEmpty(html) ? html : _sanitizer.Sanitize(html);
}
