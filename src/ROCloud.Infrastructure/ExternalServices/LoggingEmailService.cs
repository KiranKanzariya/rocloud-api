using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.ExternalServices;

/// <summary>
/// v1 email stub — logs instead of sending. The real SendGrid implementation lands in
/// Phase 14 (guide §14). Swapping it is a one-line DI change.
/// </summary>
public class LoggingEmailService : IEmailService
{
    private readonly ILogger<LoggingEmailService> _logger;

    public LoggingEmailService(ILogger<LoggingEmailService> logger) => _logger = logger;

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        _logger.LogInformation("[EMAIL STUB] To={To} Subject={Subject}", to, subject);
        return Task.CompletedTask;
    }
}
