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

public sealed record CustomerStatsDto(
    int LifetimeJarsDelivered,
    decimal LifetimePayments,
    decimal AverageMonthlySpend,
    // Item-wise breakdown of the lifetime jars delivered (issued) to this customer, per product.
    IReadOnlyList<JarsDeliveredByProductDto> JarsDeliveredByProduct);

/// <summary>Lifetime jars delivered (issued) to a customer for one product.</summary>
public sealed record JarsDeliveredByProductDto(string ProductName, string BottleSize, int Quantity);

/// <summary>Net jars a customer still holds for one product (Σ Issue − Σ Return). Guide §9.</summary>
public sealed record CustomerJarBalanceDto(Guid ProductId, string ProductName, string BottleSize, int Outstanding);

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
