using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace ROCloud.Infrastructure.ExternalServices;

/// <summary>
/// SendGrid email (guide §14). Degrades to logging when SendGrid:ApiKey is not configured,
/// so development and tests run without external credentials.
/// </summary>
public class SendGridEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SendGridEmailService> _logger;

    public SendGridEmailService(IConfiguration config, ILogger<SendGridEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        => SendAsync(to, subject, htmlBody, Array.Empty<EmailAttachment>(), ct);

    public async Task SendAsync(
        string to, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment> attachments, CancellationToken ct = default)
    {
        var apiKey = _config["SendGrid:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("[EMAIL not sent — SendGrid unconfigured] To={To} Subject={Subject}", to, subject);
            return;
        }

        var from = new EmailAddress(
            _config["SendGrid:FromEmail"] ?? "no-reply@rocloud.app",
            _config["SendGrid:FromName"] ?? "ROCloud");
        var msg = MailHelper.CreateSingleEmail(
            from, new EmailAddress(to), subject, plainTextContent: EmailHtml.ToPlainText(htmlBody), htmlBody);
        foreach (var a in attachments)
            msg.AddAttachment(a.FileName, Convert.ToBase64String(a.Content), a.ContentType);

        var client = new SendGridClient(apiKey);
        var response = await client.SendEmailAsync(msg, ct);
        if ((int)response.StatusCode >= 400)
            _logger.LogError("SendGrid failed ({Status}) sending to {To}", response.StatusCode, to);
    }
}
