using ROCloud.Application.Common;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Payments;

/// <summary>
/// Building the Payment row itself — shared by every path that takes money off a customer at the
/// counter or the door.
///
/// <para>It exists because the fields are easy to get subtly wrong and impossible to notice when you
/// do: PaidAt has to be midday-of-day for a backdated collection or the money reports on the wrong
/// date, PaymentPreference is copied off the customer for reporting, and CollectedBy is who to ask
/// when the cash does not add up. A second copy of this that drifted would produce payments that look
/// right and reconcile wrong.</para>
/// </summary>
internal static class PaymentRecording
{
    /// <summary>
    /// Adds a Completed payment for the customer. Does NOT save, and does NOT reconcile — the caller
    /// commits it together with whatever else made the money change hands, and runs
    /// <c>InvoiceAllocationSync</c> afterwards so the invoices agree.
    /// </summary>
    public static Payment Add(
        IAppDbContext db,
        ITenantContext tenant,
        ICurrentUserService currentUser,
        Customer customer,
        decimal amount,
        PaymentMethod method,
        DateOnly? paidOn,
        string? notes,
        Guid? invoiceId = null,
        Guid? orderId = null,
        string? referenceNumber = null)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            CustomerId = customer.Id,
            InvoiceId = invoiceId,
            OrderId = orderId,
            Amount = amount,
            PaymentMethod = method,
            PaymentPreference = customer.PaymentPreference,
            Status = PaymentStatus.Completed,
            ReferenceNumber = referenceNumber,
            CollectedBy = currentUser.UserId,
            // Backdated → midday of that calendar day (app zone) so it reports on the right day; else now.
            PaidAt = paidOn is { } on ? AppTimeZone.MiddayUtc(on) : DateTime.UtcNow,
            Notes = notes,
        };
        db.Payments.Add(payment);
        return payment;
    }
}
