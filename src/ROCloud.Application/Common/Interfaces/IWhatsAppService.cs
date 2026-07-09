namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// WhatsApp messaging via MSG91 (guide §14) — invoice delivery, reminders, delivery
/// confirmations. Logs instead of sending when no credentials are configured.
/// </summary>
public interface IWhatsAppService
{
    Task SendAsync(string mobile, string message, CancellationToken ct = default);
}
