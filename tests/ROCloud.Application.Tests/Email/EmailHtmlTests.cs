using ROCloud.Application.Common.Interfaces;
using ROCloud.Infrastructure.ExternalServices;

namespace ROCloud.Application.Tests.Email;

/// <summary>The branded shell + plain-text derivation used by every outgoing email (Phase 1/2a).</summary>
public class EmailHtmlTests
{
    [Fact]
    public void Wrap_WithBrand_ShowsBusinessNameInHeader()
    {
        var html = EmailHtml.Wrap("Body", new EmailBrand("AquaPure Water"));

        Assert.Contains("AquaPure Water", html);
    }

    [Fact]
    public void Wrap_Default_ShowsRoCloudInHeader()
    {
        Assert.Contains("ROCloud", EmailHtml.Wrap("Body"));
    }

    [Fact]
    public void Wrap_EncodesBrandName()
    {
        var html = EmailHtml.Wrap("Body", new EmailBrand("Ram & Sons"));

        Assert.Contains("Ram &amp; Sons", html);
        Assert.DoesNotContain("Ram & Sons", html);   // raw ampersand must be encoded
    }

    [Fact]
    public void Wrap_EmbedsContent_InTheShell()
    {
        var html = EmailHtml.Wrap("Hello there");

        Assert.Contains("Hello there", html);
        Assert.Contains("<!doctype html>", html);
        Assert.Contains("please do not reply", html);   // footer present
    }

    [Fact]
    public void Wrap_ConvertsNewlinesToLineBreaks()
    {
        var html = EmailHtml.Wrap("Line one\nLine two");

        Assert.Contains("Line one<br>", html);   // paragraph break preserved
        Assert.Contains("Line two", html);
    }

    [Fact]
    public void Wrap_LeavesHtmlFragmentIntact()
    {
        // Existing callers pass HTML fragments (e.g. an invite link) — must not be escaped/broken.
        var html = EmailHtml.Wrap("Click <a href=\"https://x/go\">here</a> now");

        Assert.Contains("<a href=\"https://x/go\">here</a>", html);
    }

    [Fact]
    public void ToPlainText_StripsTags_AndKeepsLinkUrl()
    {
        var text = EmailHtml.ToPlainText("Hi <a href=\"https://x/pay\">Pay now</a> today");

        Assert.Equal("Hi Pay now (https://x/pay) today", text);
        Assert.DoesNotContain("<", text);
    }

    [Fact]
    public void ToPlainText_DecodesEntities()
    {
        var text = EmailHtml.ToPlainText("Ram &amp; Sons &lt;water&gt;");

        Assert.Equal("Ram & Sons <water>", text);
    }
}
