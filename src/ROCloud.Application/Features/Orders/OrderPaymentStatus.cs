using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Orders;

/// <summary>
/// Derives a per-order payment state from the order total vs. the payments recorded against it
/// (guide §9). Lets the owner spot delivered-but-unpaid orders — common for PerBottle / Weekly /
/// Combined customers who don't pay at the door and aren't auto-invoiced.
/// </summary>
public static class OrderPaymentStatus
{
    public const string Paid = "Paid";
    public const string Partial = "Partial";
    public const string Unpaid = "Unpaid";
    public const string Cancelled = "Cancelled";

    /// <summary>Payment is tracked on the order's invoice, not at the order level.</summary>
    public const string Invoiced = "Invoiced";

    /// <summary>
    /// Status of a delivered, NOT-yet-invoiced order from the amount FIFO-allocated to it from the
    /// customer's payment pool (see <see cref="OrderPaymentAllocator"/>). Lets a single lump-sum or
    /// advance payment mark older orders Paid even though it isn't linked to each one.
    /// </summary>
    public static string FromAllocation(decimal total, decimal allocated)
    {
        if (total > 0m && allocated >= total) return Paid;
        if (allocated > 0m) return Partial;
        return Unpaid;
    }

    /// <summary>
    /// The (amount applied, status) shown for an order. Cancelled/non-delivered carry no payment
    /// state; invoiced orders defer to their invoice; delivered uninvoiced orders use the FIFO-applied
    /// <paramref name="allocated"/> amount.
    /// </summary>
    public static (decimal AmountPaid, string Status) Resolve(
        OrderStatus status, bool invoiced, decimal total, decimal allocated)
    {
        if (status == OrderStatus.Cancelled) return (0m, Cancelled);
        if (status != OrderStatus.Delivered) return (0m, Unpaid);
        if (invoiced) return (0m, Invoiced);
        return (allocated, FromAllocation(total, allocated));
    }
}
