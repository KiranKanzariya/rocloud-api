namespace ROCloud.Application.Features.Statements.Dtos;

/// <summary>
/// A customer's delivery statement for a date range: what was supplied, day by day. It is a record of
/// SUPPLY, never a demand for payment — no discount, no GST, no invoice number, no balance. That keeps
/// it safe to issue any number of times, for any range, overlapping invoices freely, because it touches
/// no ledger. The footer points at the invoice(s) that actually bill these orders (§ the real tax
/// document), which is what a customer's employer needs for reimbursement.
/// </summary>
public sealed record StatementPdfModel(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateOnly IssuedOn,
    string BusinessName,
    string? BusinessAddress,
    string? BusinessGstin,
    string CustomerName,
    string? CustomerCode,
    string? CustomerMobile,
    string? CustomerAddress,
    IReadOnlyList<StatementLine> Lines,
    IReadOnlyList<StatementReturnLine> StandaloneReturns,
    IReadOnlyList<StatementProductTotal> ProductTotals,
    int TotalDelivered,
    int TotalReturned,
    decimal TotalAmount,
    // Invoice numbers whose period covers any part of this range, and how many of the orders listed
    // here are not on one yet — together they decide what the footer can honestly claim.
    IReadOnlyList<string> InvoiceNumbers,
    int UninvoicedOrderCount,
    string? BrandColor);

/// <param name="Delivered">Jars handed over — the billed quantity (delivery rewrites it to what was given).</param>
/// <param name="Returned">Empties taken back on that same delivery. Standalone returns are listed separately.</param>
public sealed record StatementLine(
    DateOnly Date, string Description, int Delivered, int Returned, decimal Rate, decimal Amount);

/// <summary>An empty-jar return recorded outside a delivery (the customer page's Record return).</summary>
public sealed record StatementReturnLine(DateOnly Date, string Description, int Quantity, bool Damaged);

/// <summary>Per-product delivered total for the range — the summary line a customer checks first.</summary>
public sealed record StatementProductTotal(string Description, int Delivered);
