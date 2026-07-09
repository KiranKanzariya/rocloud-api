namespace ROCloud.Application.Features.ServiceRequests.Dtos;

/// <summary>A row in the service-requests list / board.</summary>
public sealed record ServiceRequestListItemDto(
    Guid Id,
    string TicketNumber,
    Guid CustomerId,
    string CustomerName,
    string? CustomerMobile,
    string Title,
    string ServiceType,
    string Status,
    string Priority,
    Guid? AssignedTechId,
    string? AssignedTechName,
    DateOnly? ScheduledDate,
    DateTime CreatedAt);

/// <summary>Full service request for the detail view.</summary>
public sealed record ServiceRequestDto(
    Guid Id,
    string TicketNumber,
    Guid CustomerId,
    string CustomerName,
    string? CustomerMobile,
    string Title,
    string? Description,
    string ServiceType,
    string Status,
    string Priority,
    Guid? AssignedTechId,
    string? AssignedTechName,
    DateOnly? ScheduledDate,
    DateTime? ResolvedAt,
    string? ResolutionNotes,
    DateTime CreatedAt);

/// <summary>Filter/paging for the service-requests list.</summary>
public sealed record ServiceRequestFilterDto
{
    public string? Status { get; init; }
    public string? Priority { get; init; }
    public string? ServiceType { get; init; }
    public Guid? AssignedTechId { get; init; }
    public Guid? CustomerId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

/// <summary>Result of a bulk AMC scheduling run.</summary>
public sealed record AmcScheduleResultDto(int VisitsCreated, int CustomersConsidered, int Skipped);
