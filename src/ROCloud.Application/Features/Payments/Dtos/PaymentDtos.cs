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
    DateTime PaidAt,
    /// <summary>
    /// Free-text note. Two sources: what the collector typed, and warnings the reconcile / Razorpay
    /// confirm paths append (e.g. a possible duplicate payment where a refund may be due). This was
    /// stored but never returned, so those warnings could not be read by anyone.
    /// </summary>
    string? Notes = null);

/// <summary>Filter/paging for the payments list.</summary>
public sealed record PaymentFilterDto
{
    public Guid? CustomerId { get; init; }
    /// <summary>Payments booked against one invoice — lets the invoice page ask for its OWN receipts.</summary>
    public Guid? InvoiceId { get; init; }
    public string? PaymentMethod { get; init; }
    public string? Status { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

/// <summary>
/// Collection totals for a date window, summed in SQL over EVERY matching payment — never over a
/// fetched page, which would silently under-report as soon as the window exceeds one page.
/// Completed payments only.
/// </summary>
public sealed record PaymentSummaryDto(
    decimal Collected,
    int Count,
    decimal Cash,
    decimal Upi,
    decimal Other);

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
