using ROCloud.Application.Common.Models;

namespace ROCloud.Application.Features.Platform.Billing.Dtos;

/// <summary>A platform billing transaction row (guide §26).</summary>
public sealed record BillingTransactionDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string PlanType,
    decimal Amount,
    string BillingCycle,
    string Status,
    string? RazorpayPaymentId,
    DateTime CreatedAt);

/// <summary>Billing list plus headline totals.</summary>
public sealed record BillingPageDto(
    PagedResult<BillingTransactionDto> Transactions,
    decimal TotalRevenue,
    decimal ThisMonthRevenue,
    int FailedCount,
    int RefundedCount);

/// <summary>Filter/paging for the billing list.</summary>
public sealed record BillingFilterDto
{
    public string? Status { get; init; }
    public Guid? TenantId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
