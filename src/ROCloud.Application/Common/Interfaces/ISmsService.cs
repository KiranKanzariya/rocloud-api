namespace ROCloud.Application.Common.Interfaces;

/// <summary>Transactional SMS (OTPs, critical alerts) via MSG91 (guide §14). Logs when unconfigured.</summary>
public interface ISmsService
{
    Task SendAsync(string mobile, string message, CancellationToken ct = default);
}
