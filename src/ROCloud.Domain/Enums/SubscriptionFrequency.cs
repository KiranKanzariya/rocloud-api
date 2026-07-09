namespace ROCloud.Domain.Enums;

/// <summary>How often a customer subscription recurs. DB: customer_subscriptions.frequency.</summary>
public enum SubscriptionFrequency
{
    Daily,
    AlternateDay,
    Weekly,
    Monthly,
    Custom
}
