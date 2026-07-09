using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.Time;

/// <summary>System-clock implementation of <see cref="IDateTimeProvider"/> (UTC).</summary>
public class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
