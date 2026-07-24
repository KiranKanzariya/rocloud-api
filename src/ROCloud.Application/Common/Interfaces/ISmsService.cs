namespace ROCloud.Application.Common.Interfaces;

/// <summary>Transactional SMS (OTPs, critical alerts) via MSG91 (guide §14). Logs when unconfigured.</summary>
public interface ISmsService
{
    /// <summary>
    /// Sends the SMS. Returns true only when the provider accepted it — false when unconfigured, the
    /// master switch is off, or the provider rejected/errored. Failures are reported rather than
    /// thrown so one bad number can't abort a bulk run; see <see cref="IEmailService.SendAsync"/>.
    /// </summary>
    Task<bool> SendAsync(string mobile, string message, CancellationToken ct = default);
}
