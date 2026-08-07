using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Subscription.Services;

/// <summary>
/// One rule, in one place: <b>when a tenant's term or plan moves, their open renewal invoice is no
/// longer true — cancel it, say why, and let the renewal job raise a correct one.</b>
///
/// <para>A Pending invoice is a quote for a specific period at a specific plan's price. Anything that
/// changes either of those makes it wrong, in one of two ways: it bills a period now already covered,
/// or it quotes a plan the tenant no longer holds.</para>
///
/// <para>Leaving it open does more than confuse — it BLOCKS the next real invoice. SubscriptionExpiryJob
/// skips any tenant that already has an open Pending invoice (and <c>ux_subscription_invoices_open_period</c>
/// backstops that at the database), so a stale one silently starves the tenant of the renewal invoice
/// they were supposed to receive, and they lapse without ever being billed correctly.</para>
///
/// <para>Cancelling rather than refusing the change is deliberate. Blocking would deny an admin the
/// ability to comp a lapsed tenant — whose invoice stays open indefinitely, so the block would be
/// permanent — exactly when a goodwill month is most useful. It would also refuse an overdue owner's
/// upgrade, which is the very payment being chased.</para>
/// </summary>
public static class OpenSubscriptionInvoices
{
    /// <summary>
    /// Cancels every open (Pending) subscription invoice for the tenant, recording why.
    ///
    /// <para>The reason is required, not optional. This invoice was emailed to the owner as a bill; it
    /// now appears cancelled on their billing page and prints CANCELLED on its PDF. Without a sentence
    /// explaining which action withdrew it, that reads as a billing fault rather than the gift or plan
    /// change it actually was.</para>
    ///
    /// <para>Does NOT save — the caller commits it in the same transaction as the change that made the
    /// invoice stale, so the tenant's dates and their invoices can never disagree.</para>
    /// </summary>
    /// <param name="reason">
    /// One sentence, owner-facing, ≤300 chars (the column width). Name the action and its effect on
    /// this period — "1 free month granted by ROCloud, covering this period", not "superseded".
    /// </param>
    public static async Task CancelAsync(IAppDbContext db, Guid tenantId, string reason, CancellationToken ct)
    {
        var open = await db.SubscriptionInvoices
            .Where(i => i.TenantId == tenantId && i.Status == SubscriptionInvoiceStatus.Pending)
            .ToListAsync(ct);

        foreach (var invoice in open)
        {
            invoice.Status = SubscriptionInvoiceStatus.Cancelled;
            invoice.CancellationReason = Trim(reason);
        }
    }

    /// <summary>Guards the 300-char column: a reason is a courtesy, never a reason to fail the gift or
    /// the plan change that is being committed alongside it.</summary>
    private static string Trim(string reason) =>
        reason.Length <= 300 ? reason : string.Concat(reason.AsSpan(0, 297), "...");
}
