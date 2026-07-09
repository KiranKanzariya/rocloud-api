namespace ROCloud.Application.Common.Sanitisation;

/// <summary>Strips dangerous HTML (scripts, event handlers, javascript: URIs) from user input (guide §10.5).</summary>
public interface IHtmlSanitizer
{
    string Sanitize(string html);
}
