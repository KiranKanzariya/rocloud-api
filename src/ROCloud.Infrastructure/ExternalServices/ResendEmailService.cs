using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.ExternalServices;

/// <summary>
/// Resend email (guide §14) via the REST API — Resend ships no official .NET SDK, so this uses
/// a typed HttpClient like RazorpayService/Msg91*Service. Degrades to logging when Resend:ApiKey
/// is not configured, so development and tests run without external credentials.
/// </summary>
public class ResendEmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(HttpClient http, IConfiguration config, ILogger<ResendEmailService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        => SendAsync(to, subject, htmlBody, Array.Empty<EmailAttachment>(), ct);

    public async Task SendAsync(
        string to, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment> attachments, CancellationToken ct = default)
    {
        var apiKey = _config["Resend:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("[EMAIL not sent — Resend unconfigured] To={To} Subject={Subject}", to, subject);
            return;
        }

        var fromEmail = _config["Resend:FromEmail"] ?? "no-reply@rocloud.app";
        var fromName = _config["Resend:FromName"] ?? "ROCloud";

        // Dictionary (not an anonymous type) so "attachments" is only present when there are files —
        // Resend expects each attachment's content as a base64 string (guide §14).
        var payload = new Dictionary<string, object?>
        {
            ["from"] = $"{fromName} <{fromEmail}>",
            ["to"] = new[] { to },
            ["subject"] = subject,
            ["html"] = htmlBody,
            ["text"] = EmailHtml.ToPlainText(htmlBody),
        };
        if (attachments is { Count: > 0 })
        {
            payload["attachments"] = attachments.Select(a => new
            {
                filename = a.FileName,
                content = Convert.ToBase64String(a.Content),
                content_type = a.ContentType,
            }).ToArray();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Resend failed ({Status}) sending to {To}: {Body}", response.StatusCode, to, body);
        }
    }
}
