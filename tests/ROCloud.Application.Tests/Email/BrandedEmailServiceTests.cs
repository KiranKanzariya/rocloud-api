using Microsoft.Extensions.Logging.Abstractions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Tests.Auth;
using ROCloud.Infrastructure.ExternalServices;

namespace ROCloud.Application.Tests.Email;

/// <summary>The decorator brands every email from the scope's brand context (Phase 2a) and honours
/// the Notifications:EmailEnabled master switch.</summary>
public class BrandedEmailServiceTests
{
    private sealed class CapturingEmail : IEmailService
    {
        public string? Html;
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            Html = htmlBody;
            return Task.CompletedTask;
        }
    }

    private static BrandedEmailService Build(IEmailService inner, IEmailBrandContext brand, bool emailEnabled = true)
        => new(inner, brand, new FakeAppSettings { EmailEnabled = emailEnabled }, NullLogger<BrandedEmailService>.Instance);

    [Fact]
    public async Task Wraps_WithBrandFromContext()
    {
        var capture = new CapturingEmail();
        var brand = new EmailBrandContext { Current = new EmailBrand("AquaPure Water") };

        await Build(capture, brand).SendAsync("c@x.com", "Invoice", "Hello");

        Assert.Contains("AquaPure Water", capture.Html!);   // tenant brand in the header
        Assert.Contains("Hello", capture.Html!);            // body still wrapped in
    }

    [Fact]
    public async Task DefaultsToRoCloudBrand()
    {
        var capture = new CapturingEmail();

        await Build(capture, new EmailBrandContext()).SendAsync("o@x.com", "Reset", "hi");

        Assert.Contains("ROCloud", capture.Html!);
    }

    [Fact]
    public async Task DoesNotSend_WhenEmailDisabled()
    {
        var capture = new CapturingEmail();

        await Build(capture, new EmailBrandContext(), emailEnabled: false).SendAsync("o@x.com", "Reset", "hi");

        Assert.Null(capture.Html);   // master switch off → inner provider never called
    }
}
