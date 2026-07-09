namespace ROCloud.Application.Features.Payments.Dtos;

/// <summary>A row in the payments list.</summary>
public sealed record PaymentListItemDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    Guid? InvoiceId,
    string? InvoiceNumber,
    Guid? OrderId,
    decimal Amount,
    string PaymentMethod,
    string Status,
    string? ReferenceNumber,
    Guid? CollectedBy,
    DateTime PaidAt);

/// <summary>Filter/paging for the payments list.</summary>
public sealed record PaymentFilterDto
{
    public Guid? CustomerId { get; init; }
    public string? PaymentMethod { get; init; }
    public string? Status { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

/// <summary>A customer with overdue unpaid invoices (older than the threshold).</summary>
public sealed record OutstandingDueDto(
    Guid CustomerId,
    string CustomerName,
    string? CustomerMobile,
    int InvoiceCount,
    decimal OutstandingAmount,
    DateOnly OldestDueDate,
    int DaysOverdue,
    string? CustomerLanguage = null,
    string? CustomerEmail = null);

/// <summary>Checkout parameters returned to the Angular client to open Razorpay.</summary>
public sealed record RazorpayInitiateResultDto(
    Guid PaymentId, string KeyId, string OrderId, long AmountPaise, string Currency);
