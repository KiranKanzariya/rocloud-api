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
    /// <summary>
    /// What the customer owed on everything else when this invoice was raised — frozen at generation.
    /// Never part of <see cref="TotalAmount"/>; those dues are billed on their own invoices.
    /// </summary>
    decimal PreviousDue,
    /// <summary>
    /// <see cref="PreviousDue"/> + <see cref="Balance"/>: the one figure the customer was asked for on
    /// the day. Because PreviousDue is a snapshot, this is what the PDF says forever — the screen should
    /// show the live balance beside it once older dues are settled.
    /// </summary>
    decimal NetPayable,
    string Status,
    string? GstNumber,
    string? Notes,
    DateTime CreatedAt,
    IReadOnlyList<InvoiceLineItemDto> LineItems);

/// <summary>A reconstructed line item (period deliveries grouped by product).</summary>
public sealed record InvoiceLineItemDto(
    string ProductName, string BottleSize, int Quantity, decimal Rate, decimal Amount, string? Hsn = null);

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
    string? Notes,
    // Document framing + payment state
    bool IsTaxInvoice,        // GST-registered tenant → "TAX INVOICE" + HSN; else "BILL OF SUPPLY"
    string Status,            // invoice status (Paid / PartiallyPaid / Sent / Overdue / Cancelled …)
    decimal PaidAmount,
    string? BrandColor,       // tenant's brand hex (#RRGGBB) for accents; null → app navy
    // Scan-to-pay (§10). Payload is the upi://pay deep link the QR encodes — null when the tenant has
    // no UPI id, has the QR switched off, or nothing is left to pay. UpiVpa is printed as TEXT beside
    // the QR so a customer can read where the money goes when the scan fails, or check it before paying.
    string? UpiPayload = null,
    string? UpiVpa = null,
    /// <summary>
    /// What the customer owed on everything else when this invoice was raised — a snapshot, so a reprint
    /// still shows what this document originally demanded. 0 skips the block entirely, which is what
    /// keeps every invoice issued before this feature looking exactly as it did.
    /// </summary>
    decimal PreviousDue = 0m);

public sealed record InvoicePdfLine(
    string Description, string Hsn, int Quantity, decimal Rate, decimal Amount);
