namespace ROCloud.Application.Common.Interfaces;

/// <summary>Abstraction over the system clock (for testability). Returns UTC.</summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
