using Ganss.Xss;

namespace ROCloud.Application.Common.Sanitisation;

/// <summary>
/// HTML sanitiser (guide §10.5) backed by Ganss.Xss. Removes scripts, event-handler
/// attributes and javascript: URIs from user-provided HTML before it is stored, preventing
/// stored XSS. Thread-safe and registered as a singleton.
/// </summary>
public class HtmlSanitizerService : IHtmlSanitizer
{
    private readonly HtmlSanitizer _sanitizer = new();

    public string Sanitize(string html) => string.IsNullOrEmpty(html) ? html : _sanitizer.Sanitize(html);
}
