namespace ROCloud.Application.Features.Platform.Tenants.Dtos;

/// <summary>A tenant row in the platform tenants list (guide §26).</summary>
public sealed record TenantListItemDto(
    Guid Id,
    string Name,
    string Subdomain,
    string PlanName,
    string PlanType,
    string Status,
    string OwnerName,
    string OwnerEmail,
    int CustomerCount,
    DateTime CreatedAt);

/// <summary>Full tenant detail for the platform admin.</summary>
public sealed record TenantDetailDto(
    Guid Id,
    string Name,
    string Subdomain,
    string PlanName,
    string PlanType,
    string Status,
    string OwnerName,
    string OwnerEmail,
    string OwnerMobile,
    string? GstNumber,
    bool GstEnabled,
    decimal GstPercent,
    string? AddressLine,
    string? City,
    string? State,
    string? Pincode,
    int CustomerCount,
    int UserCount,
    DateTime? TrialEndsAt,
    DateTime? SubscriptionEndsAt,
    DateTime CreatedAt,
    decimal MonthlyPrice,
    string SubscriptionDiscountType,
    decimal SubscriptionDiscountValue,
    decimal NetMonthlyPrice,
    // Lifetime billing summary for this tenant (all their transactions, not a page).
    decimal TotalPaid,
    int BillingCount);

/// <summary>Filter/paging for the platform tenants list.</summary>
public sealed record TenantFilterDto
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public string? PlanType { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
