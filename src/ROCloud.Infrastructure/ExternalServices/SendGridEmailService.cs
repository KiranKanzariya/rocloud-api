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
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<SendGridEmailService> _logger;

    // The HttpClient comes from IHttpClientFactory (see DependencyInjection) and is handed to the
    // SendGrid SDK, rather than letting `new SendGridClient(apiKey)` build its own per send: a
    // per-send client leaks sockets under load and never picks up DNS changes. It also means the
    // factory's configured timeout actually applies to SendGrid.
    public SendGridEmailService(HttpClient http, IConfiguration config, ILogger<SendGridEmailService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        => SendAsync(to, subject, htmlBody, Array.Empty<EmailAttachment>(), ct);

    public async Task<bool> SendAsync(
        string to, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment> attachments, CancellationToken ct = default)
    {
        var apiKey = _config["SendGrid:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("[EMAIL not sent — SendGrid unconfigured] To={To} Subject={Subject}", to, subject);
            return false;
        }

        var from = new EmailAddress(
            _config["SendGrid:FromEmail"] ?? "no-reply@rocloud.app",
            _config["SendGrid:FromName"] ?? "ROCloud");
        var msg = MailHelper.CreateSingleEmail(
            from, new EmailAddress(to), subject, plainTextContent: EmailHtml.ToPlainText(htmlBody), htmlBody);
        foreach (var a in attachments)
            msg.AddAttachment(a.FileName, Convert.ToBase64String(a.Content), a.ContentType);

        try
        {
            var client = new SendGridClient(_http, apiKey);
            var response = await client.SendEmailAsync(msg, ct);
            if ((int)response.StatusCode < 400) return true;

            _logger.LogError("SendGrid failed ({Status}) sending to {To}", response.StatusCode, to);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "SendGrid transport failure sending to {To}", to);
            return false;
        }
    }
}
