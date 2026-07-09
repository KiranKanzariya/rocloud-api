namespace ROCloud.Application.Common.Exceptions;

/// <summary>Thrown when an action would exceed the tenant's subscription plan limit (e.g. max users).</summary>
public class PlanLimitException : Exception
{
    public PlanLimitException(string message) : base(message) { }
}
