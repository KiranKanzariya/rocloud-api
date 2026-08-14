namespace ROCloud.Application.Features.Customers.Dtos;

/// <summary>Lightweight row for the customer list.</summary>
public sealed record CustomerListItemDto(
    Guid Id,
    string? CustomerCode,
    string Name,
    string? Mobile,
    string? AreaName,
    string? PreferredBottleSize,
    string DeliveryMode,
    string PaymentPreference,
    decimal Balance,
    bool IsActive,
    string DiscountType,
    decimal DiscountValue,
    int JarsOut);

/// <summary>Full customer for the detail view, including subscriptions and recent activity.</summary>
public sealed record CustomerDto(
    Guid Id,
    string? CustomerCode,
    string Name,
    string? Mobile,
    string? AlternateMobile,
    string? Email,
    string? AddressLine,
    string? Landmark,
    decimal? Latitude,
    decimal? Longitude,
    Guid? AreaId,
    string? AreaName,
    string DeliveryMode,
    string PaymentPreference,
    string? PreferredBottleSize,
    string? PreferredLanguage,
    string? Notes,
    bool IsActive,
    decimal Balance,
    string DiscountType,
    decimal DiscountValue,
    DateTime CreatedAt,
    IReadOnlyList<CustomerSubscriptionDto> Subscriptions,
    IReadOnlyList<CustomerOrderSummaryDto> RecentOrders,
    IReadOnlyList<CustomerPaymentSummaryDto> RecentPayments);

public sealed record CustomerSubscriptionDto(
    Guid Id, string ProductName, int Quantity, string Frequency, decimal RatePerUnit, bool IsActive);

public sealed record CustomerOrderSummaryDto(Guid Id, DateOnly OrderDate, string Status);

/// <param name="Notes">
/// Collector's remark, plus any warning appended by the reconcile / Razorpay confirm paths (see
/// PaymentNotes). Carried here too so the customer's payment history shows what the payments list does.
/// </param>
public sealed record CustomerPaymentSummaryDto(
    Guid Id, decimal Amount, string PaymentMethod, DateTime PaidAt, string? Notes = null);

/// <param name="MonthJarsDelivered">
/// Jars issued so far in the current calendar month (app timezone, see AppTimeZone) — the running
/// total the owner watches against the month's billing, alongside the lifetime figure.
/// </param>
public sealed record CustomerStatsDto(
    int LifetimeJarsDelivered,
    int MonthJarsDelivered,
    decimal LifetimePayments,
    decimal AverageMonthlySpend,
    // Item-wise breakdown of the jars delivered (issued) to this customer, per product.
    IReadOnlyList<JarsDeliveredByProductDto> JarsDeliveredByProduct);

/// <summary>Jars delivered (issued) to a customer for one product: lifetime, and this month.</summary>
public sealed record JarsDeliveredByProductDto(
    string ProductName, string BottleSize, int Quantity, int MonthQuantity);

/// <summary>Net jars a customer still holds for one product (Σ Issue − Σ Return). Guide §9.</summary>
public sealed record CustomerJarBalanceDto(Guid ProductId, string ProductName, string BottleSize, int Outstanding);

/// <summary>
/// One customer-month of jar movement: what went out, what came back, what is still held, and what it
/// cost — the single view that answers "what happened on the 4th?" without cross-referencing three tabs.
/// </summary>
public sealed record CustomerLedgerDto(
    string Month,                 // "YYYY-MM", echoed back so a late response can be matched to its request
    int OpeningJarsOut,           // jars the customer held before the month started
    int ClosingJarsOut,           // …and after it ended (== the jar-balance endpoint for the current month)
    int TotalPut,
    int TotalEmp,
    decimal TotalAmount,
    IReadOnlyList<CustomerLedgerRowDto> Rows,
    IReadOnlyList<CustomerLedgerProductDto> Products);

/// <summary>
/// One product's month in summary — the heading of its group in the ledger. Totalled server-side in the
/// same pass that produces the rows, so the portal and the app cannot arrive at different subtotals by
/// each adding up the rows their own way.
/// </summary>
/// <param name="Opening">Jars of this product held before the month began.</param>
/// <param name="Closing">…and after it ended. Equals this product's line in the jar-balance endpoint.</param>
public sealed record CustomerLedgerProductDto(
    Guid ProductId,
    string ProductName,
    string BottleSize,
    int Opening,
    int Closing,
    int Put,
    int Emp,
    decimal Amount);

/// <summary>
/// One movement of jars. <paramref name="Put"/> and <paramref name="Emp"/> are mutually exclusive: a row
/// is either a hand-over or a collection, never both, because that is how the underlying movements are
/// recorded and merging them would hide a same-day return.
/// </summary>
/// <param name="Rem">Jars of THIS product still held after this row — a running total, computed server-side
/// in one ordered pass so two clients can never disagree about it.</param>
/// <param name="Amount">What was charged for this row. Empties carry 0: returning a jar is not a sale.</param>
/// <param name="Invoiced">
/// Whether an invoice period covers this date. Deliberately not "Paid": payments settle against a
/// customer's oldest dues, not against a particular delivery, so a per-row paid flag would be invented.
/// </param>
public sealed record CustomerLedgerRowDto(
    DateOnly Date,
    string Kind,                  // "Delivery" | "Return" | "Damage"
    Guid ProductId,
    string ProductName,
    string BottleSize,
    int Put,
    int Emp,
    int Rem,
    decimal Amount,
    bool Invoiced);

/// <summary>Filter/paging/sort options for the customer list.</summary>
public sealed record CustomerFilterDto
{
    public Guid? AreaId { get; init; }
    public bool? IsActive { get; init; }
    public string? DeliveryMode { get; init; }
    public string? PaymentPreference { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string? SortBy { get; init; }
    public string? SortDir { get; init; }
}
