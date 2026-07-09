using ROCloud.Domain.Enums;

namespace ROCloud.Domain.Events;

/// <summary>Raised when a new tenant has been provisioned (for future event-driven features).</summary>
public sealed record TenantProvisionedEvent(Guid TenantId, PlanType PlanType);
