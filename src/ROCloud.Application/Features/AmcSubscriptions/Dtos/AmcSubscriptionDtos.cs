namespace ROCloud.Application.Features.AmcSubscriptions.Dtos;

/// <summary>A row in the AMC subscriptions list.</summary>
public sealed record AmcSubscriptionListItemDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string? CustomerMobile,
    string? PlanName,
    int IntervalMonths,
    decimal Amount,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? LastServiceDate,
    DateOnly NextDueDate,
    bool IsActive);

/// <summary>Filter/paging for the AMC subscriptions list.</summary>
public sealed record AmcSubscriptionFilterDto
{
    public Guid? CustomerId { get; init; }
    public bool? IsActive { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
