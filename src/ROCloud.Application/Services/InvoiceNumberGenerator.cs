using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Services;

/// <summary>
/// The single source of truth for minting a customer invoice number (<c>INV-YYYYMM-NNNN</c>, per tenant
/// per month). It advances from the current high-water mark — <c>MAX(existing suffix) + 1</c> — NOT a row
/// COUNT.
///
/// WHY not COUNT: a count assumes a gapless 1..N run. Hard-deleting or otherwise removing an invoice, or
/// numbers minted by a different path, leaves the row count BELOW the highest suffix, so <c>count + 1</c>
/// re-mints a number that still exists → <c>duplicate key … idx_invoices_number</c> (a 500). Max+1 is
/// always above every existing number, so a gap can never cause a collision.
///
/// Suffixes are zero-padded to 4 digits, so lexical order == numeric order within a month and the highest
/// row can be found with a single ORDER BY … DESC. Soft-deleted invoices are included (IgnoreQueryFilters)
/// so a cancelled/deleted number is never handed out again. Mirrors the subscription-invoice factory.
/// </summary>
public static class InvoiceNumberGenerator
{
    public static string Prefix(DateOnly invoiceDate) => $"INV-{invoiceDate:yyyyMM}-";

    /// <summary>
    /// Highest sequence number currently in use for the tenant's month (0 if none). Callers that mint many
    /// invoices in one pass read this once and then increment locally.
    /// </summary>
    public static async Task<int> MaxSeqAsync(IAppDbContext db, Guid tenantId, DateOnly invoiceDate, CancellationToken ct)
    {
        var prefix = Prefix(invoiceDate);
        var highest = await db.Invoices.IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && i.InvoiceNumber.StartsWith(prefix))
            .OrderByDescending(i => i.InvoiceNumber)
            .Select(i => i.InvoiceNumber)
            .FirstOrDefaultAsync(ct);

        return highest is not null && int.TryParse(highest[prefix.Length..], out var n) ? n : 0;
    }

    /// <summary>The next invoice number for the tenant's month (high-water mark + 1).</summary>
    public static async Task<string> NextAsync(IAppDbContext db, Guid tenantId, DateOnly invoiceDate, CancellationToken ct)
        => $"{Prefix(invoiceDate)}{await MaxSeqAsync(db, tenantId, invoiceDate, ct) + 1:D4}";

    /// <summary>
    /// True when <paramref name="ex"/> is a unique-violation on the invoice-number index — i.e. another
    /// writer grabbed the number we minted between read and save. Callers can catch this, re-mint above the
    /// new high-water mark, and retry. Matched on the stable constraint name so the Application layer needs
    /// no Npgsql dependency.
    /// </summary>
    public static bool IsDuplicateNumber(DbUpdateException ex)
        => ex.GetBaseException().Message.Contains("idx_invoices_number", StringComparison.Ordinal);
}
