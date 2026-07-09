using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Infrastructure.ExternalServices;

/// <summary>
/// SMS via MSG91 (guide §14). Gated by the Notifications:SmsEnabled master switch, then degrades to
/// logging when MSG91:AuthKey is not configured.
/// </summary>
public class Msg91SmsService : ISmsService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly IAppSettings _settings;
    private readonly ILogger<Msg91SmsService> _logger;

    public Msg91SmsService(HttpClient http, IConfiguration config, IAppSettings settings, ILogger<Msg91SmsService> logger)
    {
        _http = http;
        _config = config;
        _settings = settings;
        _logger = logger;
    }

    public async Task SendAsync(string mobile, string message, CancellationToken ct = default)
    {
        // MSG91 wants country-code + number, digits only (e.g. 919876543210).
        var recipient = MobileFormat.ToMsg91(mobile);

        if (!_settings.SmsEnabled)
        {
            _logger.LogInformation("[SMS not sent — disabled via Notifications:SmsEnabled] To={Mobile}", recipient);
            return;
        }

        var authKey = _config["MSG91:AuthKey"];
        if (string.IsNullOrWhiteSpace(authKey))
        {
            _logger.LogInformation("[SMS not sent — MSG91 unconfigured] To={Mobile}", recipient);
            return;
        }

        try
        {
            var url = _config["MSG91:SmsUrl"] is { Length: > 0 } u ? u : "https://control.msg91.com/api/v5/flow/";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new
                {
                    sender = _config["MSG91:SenderId"] ?? "ROCLOU",
                    short_url = _config["MSG91:ShortUrl"] ?? "0",
                    mobiles = recipient,
                    message
                })
            };
            req.Headers.Add("authkey", authKey);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogError("MSG91 SMS failed ({Status}) to {Mobile}", resp.StatusCode, mobile);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "MSG91 SMS error to {Mobile}", mobile);
        }
    }
}
