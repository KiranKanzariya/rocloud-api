namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// WhatsApp messaging via MSG91 (guide §14) — invoice delivery, reminders, delivery
/// confirmations. Logs instead of sending when no credentials are configured.
/// </summary>
public interface IWhatsAppService
{
    /// <summary>
    /// Sends the message. Returns true only when the provider accepted it — false when unconfigured,
    /// the master switch is off, or the provider rejected/errored. Failures are reported rather than
    /// thrown so one bad number can't abort a bulk run; see <see cref="IEmailService.SendAsync"/>.
    /// </summary>
    Task<bool> SendAsync(string mobile, string message, CancellationToken ct = default);
}
