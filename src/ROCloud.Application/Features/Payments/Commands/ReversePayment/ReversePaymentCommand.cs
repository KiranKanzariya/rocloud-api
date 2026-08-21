using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Payments.Commands.ReversePayment;

/// <summary>
/// Takes back a payment that should not have been recorded — a mis-tap on the money-in worklist, a
/// collection booked against the wrong customer, cash that turned out not to have changed hands.
///
/// <para><b>Marks, never deletes.</b> The row stays, its status becomes
/// <see cref="PaymentStatus.Refunded"/>, and the reason is appended to its notes. Money that was
/// recorded and then taken back is a fact about the day, and an owner reconciling a cash box against
/// the app needs to see that it happened — a row that simply vanishes reads as the app losing a
/// payment. It is also the difference between an audit trail and a hole in one.</para>
///
/// <para>Nothing else has to be undone. A payment touches only its own row and the invoice allocation
/// derived from it (<see cref="PaymentRecording"/> adds nothing else), and
/// <see cref="InvoiceAllocationSync"/> counts only <see cref="PaymentStatus.Completed"/> rows — so
/// flipping the status and re-running the sync withdraws the money from the customer's balance, from
/// every invoice it had settled, and from the paid/unpaid filters, in one step. The sync's own summary
/// already anticipated this: it is a full recompute precisely so it can reduce a paid amount for "a
/// payment that turned out to have failed".</para>
/// </summary>
public sealed record ReversePaymentCommand(Guid PaymentId, string? Reason = null) : IRequest;

public class ReversePaymentCommandHandler : IRequestHandler<ReversePaymentCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ReversePaymentCommandHandler> _logger;

    public ReversePaymentCommandHandler(
        IAppDbContext db,
        ICurrentUserService currentUser,
        ILogger<ReversePaymentCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task Handle(ReversePaymentCommand request, CancellationToken ct)
    {
        // Tenant query filter + explicit id → another tenant's payment is a 404, not a 403.
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == request.PaymentId, ct)
                      ?? throw new NotFoundException("Payment", request.PaymentId);

        // Reversing twice would be harmless to the balance — the sync is idempotent — but it would
        // append a second note and log a second reversal for money that was already taken back once.
        // Saying so is better than silently doing nothing.
        if (payment.Status == PaymentStatus.Refunded)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["paymentId"] = ["This payment has already been reversed."]
            });

        payment.Status = PaymentStatus.Refunded;
        payment.Notes = PaymentNotes.Append(payment.Notes, ReversalNote(request.Reason));

        // The status change and the allocation it invalidates are one fact, so they commit together —
        // the same reason CollectPayment wraps its own pair. Without it a reversal could save while the
        // sync failed, leaving the invoice still claiming to be settled by money that has been taken
        // back, and the customer un-dunned until the nightly job noticed.
        await using var tx = _db.IsRelational ? await _db.BeginTransactionAsync(ct) : null;

        await _db.SaveChangesAsync(ct);

        // Full recompute: the invoices this payment had settled fall back to Sent/PartiallyPaid, and
        // the customer's balance rises again by exactly the amount withdrawn.
        await InvoiceAllocationSync.SyncAsync(_db, payment.CustomerId, ct);

        if (tx is not null) await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Payment {PaymentId} of {Amount} for customer {CustomerId} reversed by {UserId}",
            payment.Id, payment.Amount, payment.CustomerId, _currentUser.UserId);
    }

    /// <summary>
    /// What the row says afterwards. Not tagged <see cref="PaymentNotes.ActionRequiredMarker"/>: a
    /// reversal is a completed act, not something still waiting on the owner.
    /// </summary>
    private static string ReversalNote(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? "Reversed." : $"Reversed: {reason.Trim()}";
}
