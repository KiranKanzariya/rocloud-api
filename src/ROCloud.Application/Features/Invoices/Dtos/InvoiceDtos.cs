namespace ROCloud.Application.Features.Invoices.Dtos;

/// <summary>Lightweight row for the invoices list.</summary>
public sealed record InvoiceListItemDto(
    Guid Id,
    string InvoiceNumber,
    Guid CustomerId,
    string CustomerName,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal Balance,
    string Status,
    decimal Discount,
    DateTime CreatedAt);

/// <summary>Full invoice for the detail view, with reconstructed line items.</summary>
public sealed record InvoiceDto(
    Guid Id,
    string InvoiceNumber,
    Guid CustomerId,
    string CustomerName,
    string? CustomerMobile,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    decimal SubTotal,
    decimal TaxAmount,
    decimal Discount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal Balance,
    /// <summary>
    /// How much of <see cref="PaidAmount"/> came from the customer's unallocated payment pool rather
    /// than a payment linked to this invoice — those payments won't appear in this invoice's payment
    /// list, so the UI says where the money came from instead of leaving a "Paid" with no receipts.
    /// </summary>
    decimal AllocatedFromPool,
    string Status,
    string? GstNumber,
    string? Notes,
    string? PdfUrl,
    DateTime CreatedAt,
    IReadOnlyList<InvoiceLineItemDto> LineItems);

/// <summary>A reconstructed line item (period deliveries grouped by product).</summary>
public sealed record InvoiceLineItemDto(
    string ProductName, string BottleSize, int Quantity, decimal Rate, decimal Amount);

/// <summary>Filter/paging for the invoices list.</summary>
public sealed record InvoiceFilterDto
{
    public Guid? CustomerId { get; init; }
    public string? Status { get; init; }
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

/// <summary>Everything the PDF generator needs — built in the Application layer from the invoice + tenant + lines.</summary>
public sealed record InvoicePdfModel(
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    // Seller (tenant)
    string BusinessName,
    string? BusinessGstin,
    string? BusinessAddress,
    // Buyer (customer)
    string CustomerName,
    string? CustomerMobile,
    string? CustomerGstin,
    // Lines + money
    IReadOnlyList<InvoicePdfLine> Lines,
    decimal SubTotal,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal Discount,
    decimal TotalAmount,
    string? Notes);

public sealed record InvoicePdfLine(
    string Description, string Hsn, int Quantity, decimal Rate, decimal Amount);
