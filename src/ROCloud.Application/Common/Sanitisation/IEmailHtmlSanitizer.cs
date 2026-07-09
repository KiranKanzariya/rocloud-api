namespace ROCloud.Application.Common.Sanitisation;

/// <summary>
/// A wider HTML sanitiser for admin-authored EMAIL templates: allows the table/inline-style markup
/// HTML email needs, while still stripping scripts, event handlers and javascript: URIs. Distinct from
/// the strict <see cref="IHtmlSanitizer"/> used for owner/customer rich text.
/// </summary>
public interface IEmailHtmlSanitizer
{
    string Sanitize(string html);
}
