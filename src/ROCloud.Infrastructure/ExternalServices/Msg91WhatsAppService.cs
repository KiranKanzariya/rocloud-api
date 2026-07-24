using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Infrastructure.ExternalServices;

/// <summary>
/// WhatsApp via MSG91 (guide §14) — invoices, reminders, delivery confirmations. Gated by the
/// Notifications:WhatsAppEnabled master switch (the per-tenant plan feature is checked separately by
/// callers), then degrades to logging when MSG91:AuthKey / MSG91:WhatsAppNumber are not configured.
/// </summary>
public class Msg91WhatsAppService : IWhatsAppService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly IAppSettings _settings;
    private readonly ILogger<Msg91WhatsAppService> _logger;

    public Msg91WhatsAppService(HttpClient http, IConfiguration config, IAppSettings settings, ILogger<Msg91WhatsAppService> logger)
    {
        _http = http;
        _config = config;
        _settings = settings;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string mobile, string message, CancellationToken ct = default)
    {
        // MSG91 wants country-code + number, digits only (e.g. 919876543210).
        var recipient = MobileFormat.ToMsg91(mobile);

        if (!_settings.WhatsAppEnabled)
        {
            _logger.LogInformation("[WhatsApp not sent — disabled via Notifications:WhatsAppEnabled] To={Mobile}", recipient);
            return false;
        }

        var authKey = _config["MSG91:AuthKey"];
        var fromNumber = _config["MSG91:WhatsAppNumber"];
        if (string.IsNullOrWhiteSpace(authKey) || string.IsNullOrWhiteSpace(fromNumber))
        {
            _logger.LogInformation("[WhatsApp not sent — MSG91 unconfigured] To={Mobile}", recipient);
            return false;
        }

        try
        {
            var url = _config["MSG91:WhatsAppUrl"] is { Length: > 0 } u
                ? u : "https://control.msg91.com/api/v5/whatsapp/whatsapp-outbound-message/";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new
                {
                    integrated_number = fromNumber,
                    recipient_number = recipient,
                    content_type = "text",
                    text = message
                })
            };
            req.Headers.Add("authkey", authKey);

            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return true;

            _logger.LogError("MSG91 WhatsApp failed ({Status}) to {Mobile}", resp.StatusCode, mobile);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "MSG91 WhatsApp error to {Mobile}", mobile);
            return false;
        }
    }
}
