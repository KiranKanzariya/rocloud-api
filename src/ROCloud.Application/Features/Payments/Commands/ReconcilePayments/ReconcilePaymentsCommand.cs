using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Payments.Commands.ReconcilePayments;

public sealed record ReconcilePaymentsResult(int Completed, int Failed, int Duplicates, int StillPending);

/// <summary>
/// Resolves online payments stuck in Pending for the CURRENT tenant (runs per-tenant so RLS is
/// satisfied — a no-tenant cross-tenant read returns nothing). For each stale Pending payment it
/// asks Razorpay whether the order was actually paid, then completes it (applying to the invoice),
/// marks it Failed (abandoned), or leaves it. Double-credit guard: if the invoice was already
/// settled (e.g. by cash) it records the payment but does NOT re-apply it, flagging a possible refund.
/// This is the reliable completion path — the anonymous webhook can't read payments under RLS.
/// </summary>
public sealed record ReconcilePaymentsCommand : IRequest<ReconcilePaymentsResult>;

public class ReconcilePaymentsCommandHandler : IRequestHandler<ReconcilePaymentsCommand, ReconcilePaymentsResult>
{
    // Give the webhook/checkout a chance before reconciling; give up (abandoned) after a day.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AbandonAfter = TimeSpan.FromHours(24);

    private readonly IAppDbContext _db;
    private readonly IRazorpayService _razorpay;
    private readonly ILogger<ReconcilePaymentsCommandHandler> _logger;

    public ReconcilePaymentsCommandHandler(
        IAppDbContext db, IRazorpayService razorpay, ILogger<ReconcilePaymentsCommandHandler> logger)
    {
        _db = db;
        _razorpay = razorpay;
        _logger = logger;
    }

    public async Task<ReconcilePaymentsResult> Handle(ReconcilePaymentsCommand request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var staleBefore = now - StaleAfter;

        var pending = await _db.Payments
            .Where(p => p.Status == PaymentStatus.Pending
                        && p.PaymentMethod == PaymentMethod.Online
                        && p.PaidAt < staleBefore
                        && p.ReferenceNumber != null)
            .ToListAsync(ct);

        int completed = 0, failed = 0, duplicates = 0, stillPending = 0;

        foreach (var p in pending)
        {
            RazorpayPaymentStatus status;
            try
            {
                status = await _razorpay.GetOrderPaymentStatusAsync(p.ReferenceNumber!, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconcile: Razorpay status fetch failed for payment {PaymentId}", p.Id);
                stillPending++;
                continue;
            }

            if (status.Paid)
            {
                p.RazorpayPaymentId = status.PaymentId;
                p.PaidAt = now;
                p.Status = PaymentStatus.Completed;

                if (p.InvoiceId is { } invoiceId)
                {
                    var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
                    if (invoice is not null)
                    {
                        var alreadySettled = invoice.Status == InvoiceStatus.Paid
                                             || invoice.PaidAmount >= invoice.TotalAmount;
                        if (alreadySettled)
                        {
                            p.Notes = Append(p.Notes, "Reconciled: invoice already settled — possible duplicate payment, refund may be due.");
                            duplicates++;
                            continue;   // money captured, but do NOT re-credit the invoice
                        }

                        Payments.PaymentApplication.ApplyToInvoice(invoice, p.Amount);
                    }
                }
                completed++;
            }
            else if (p.PaidAt < now - AbandonAfter)
            {
                p.Status = PaymentStatus.Failed;
                p.Notes = Append(p.Notes, "Reconciled: no payment received — checkout abandoned.");
                failed++;
            }
            else
            {
                stillPending++;
            }
        }

        if (completed + failed + duplicates > 0)
        {
            await _db.SaveChangesAsync(ct);

            // Some of these payments just became real (Completed) and some just died (Failed). Either
            // way the customers' invoices must be re-settled — SyncAsync demotes as well as promotes.
            foreach (var customerId in pending.Select(p => p.CustomerId).Distinct())
                await Payments.InvoiceAllocationSync.SyncAsync(_db, customerId, ct);
        }

        return new ReconcilePaymentsResult(completed, failed, duplicates, stillPending);
    }

    private static string Append(string? notes, string add)
        => string.IsNullOrWhiteSpace(notes) ? add : $"{notes} {add}";
}
