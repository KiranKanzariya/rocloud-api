namespace ROCloud.Domain.Events;

/// <summary>Raised when an invoice has been generated (for future event-driven features).</summary>
public sealed record InvoiceGeneratedEvent(Guid InvoiceId, Guid TenantId, Guid CustomerId);
