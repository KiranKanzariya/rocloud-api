using ROCloud.Application.Common.Sanitisation;

namespace ROCloud.Application.Tests.Sanitisation;

public class HtmlSanitizerTests
{
    [Fact]
    public void HtmlSanitizer_StripsScriptTags()
    {
        var sut = new HtmlSanitizerService();

        var dirty = "<p>Hello</p><script>alert('xss')</script><a href=\"javascript:evil()\">click</a>";
        var clean = sut.Sanitize(dirty);

        Assert.DoesNotContain("<script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", clean);   // safe content preserved
    }

    [Fact]
    public void HtmlSanitizer_RemovesEventHandlerAttributes()
    {
        var sut = new HtmlSanitizerService();

        var clean = sut.Sanitize("<img src=x onerror=\"steal()\">");

        Assert.DoesNotContain("onerror", clean, StringComparison.OrdinalIgnoreCase);
    }
}
