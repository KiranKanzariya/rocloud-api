using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Infrastructure.ExternalServices;

/// <summary>
/// Decorates the real email provider (SendGrid/Resend): wraps every outgoing body in the shared
/// branded HTML shell so all transactional email has one consistent look and plain-text bodies keep
/// their line breaks. The wrapped provider then derives the plain-text alternative part from this HTML.
/// Also the single choke point for the Notifications:EmailEnabled master switch — when it's off, no
/// email leaves the system regardless of caller.
/// </summary>
public sealed class BrandedEmailService : IEmailService
{
    private readonly IEmailService _inner;
    private readonly IEmailBrandContext _brand;
    private readonly IAppSettings _settings;
    private readonly ILogger<BrandedEmailService> _logger;

    public BrandedEmailService(
        IEmailService inner, IEmailBrandContext brand, IAppSettings settings, ILogger<BrandedEmailService> logger)
    {
        _inner = inner;
        _brand = brand;
        _settings = settings;
        _logger = logger;
    }

    public Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        // Switched off is "not sent" — report it as such, so a caller that records the send (the
        // reminder throttle) doesn't write a reminder_log row for a mail that never left.
        if (!_settings.EmailEnabled)
        {
            _logger.LogInformation("[EMAIL not sent — disabled via Notifications:EmailEnabled] To={To} Subject={Subject}", to, subject);
            return Task.FromResult(false);
        }
        return _inner.SendAsync(to, subject, EmailHtml.Wrap(htmlBody, _brand.Current), ct);
    }

    public Task<bool> SendAsync(
        string to, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment> attachments, CancellationToken ct = default)
    {
        if (!_settings.EmailEnabled)
        {
            _logger.LogInformation("[EMAIL not sent — disabled via Notifications:EmailEnabled] To={To} Subject={Subject}", to, subject);
            return Task.FromResult(false);
        }
        return _inner.SendAsync(to, subject, EmailHtml.Wrap(htmlBody, _brand.Current), attachments, ct);
    }
}
