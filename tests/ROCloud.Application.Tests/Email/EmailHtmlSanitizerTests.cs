using ROCloud.Application.Common.Sanitisation;

namespace ROCloud.Application.Tests.Email;

/// <summary>The wider email-HTML sanitiser used by the admin default-template editor (#3).</summary>
public class EmailHtmlSanitizerTests
{
    private readonly IEmailHtmlSanitizer _s = new EmailHtmlSanitizerService();

    [Fact]
    public void Allows_TableAndInlineStyle()
    {
        var clean = _s.Sanitize("<table cellpadding=\"8\"><tr><td style=\"color:red\">Hi</td></tr></table>");

        Assert.Contains("<table", clean);
        Assert.Contains("cellpadding", clean);
        Assert.Contains("style", clean);
        Assert.Contains("Hi", clean);
    }

    [Fact]
    public void Strips_ScriptsAndEventHandlers()
    {
        var clean = _s.Sanitize("<p onclick=\"steal()\">Hi</p><script>alert(1)</script>");

        Assert.DoesNotContain("<script", clean);
        Assert.DoesNotContain("onclick", clean);
        Assert.Contains("Hi", clean);
    }

    [Fact]
    public void KeepsHttpsLinks_StripsJavascriptScheme()
    {
        var clean = _s.Sanitize("<a href=\"https://x/pay\">pay</a><a href=\"javascript:alert(1)\">bad</a>");

        Assert.Contains("https://x/pay", clean);
        Assert.DoesNotContain("javascript:", clean);
    }
}
