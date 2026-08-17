using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Application.Features.Payments;
using ROCloud.Application.Services;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Inventory.Commands.RecordCustomerReturn;

/// <summary>
/// Records empty jars handed back by a customer when there is no delivery to attach them to (e.g. the
/// customer is moving house, or the return was missed on the day). Reduces that product's issued float
/// and the customer's outstanding jars, recorded as a customer-scoped Return movement — the same effect
/// as a return captured during a delivery, minus the order link. May be backdated within the platform
/// window (Billing:BackdateWindowDays); there is no billing-period gate because a return moves the bottle
/// float, not any invoice.
/// </summary>
public sealed record RecordCustomerReturnCommand(
    Guid CustomerId,
    Guid ProductId,
    int Quantity,
    DateOnly? ReturnedOn,
    string? Notes,
    /// <summary>
    /// Money handed over at the same moment, if any. A customer at the counter usually returns jars
    /// AND settles up in one breath; recording that as two separate requests is how you end up with
    /// the jars logged, the cash not, and the customer dunned for money he already paid. Null or 0
    /// means a plain return, exactly as before.
    /// </summary>
    decimal? CollectedAmount = null,
    string? PaymentMethod = null,
    /// <summary>
    /// The jar came back broken: written off instead of re-entering the reusable float. Either way the
    /// customer stops being counted as holding it. Here so there is ONE customer-return endpoint —
    /// otherwise a damaged return would have to go through the generic movements endpoint and could
    /// never carry the payment that came with it.
    /// </summary>
    bool Damaged = false) : IRequest<RecordCustomerReturnResult>;

/// <summary>The movement, plus the payment when one was taken — so the caller can confirm both.</summary>
public sealed record RecordCustomerReturnResult(Guid MovementId, Guid? PaymentId, decimal CollectedAmount);

public class RecordCustomerReturnCommandValidator : AbstractValidator<RecordCustomerReturnCommand>
{
    public RecordCustomerReturnCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.ProductId).NotEmpty();
        // Still a RETURN. Recording only a payment belongs on Collect payment, which already exists on
        // the same screen — two doors to one outcome is worse than the asymmetry.
        RuleFor(c => c.Quantity).GreaterThan(0);
        RuleFor(c => c.Notes).MaximumLength(1000);

        RuleFor(c => c.CollectedAmount).GreaterThanOrEqualTo(0m).When(c => c.CollectedAmount.HasValue);
        RuleFor(c => c.PaymentMethod)
            .Must(v => v is not null
                       && Enum.GetNames<PaymentMethod>().Contains(v)
                       && v != nameof(Domain.Enums.PaymentMethod.None))
            .When(c => c.CollectedAmount > 0m)
            .WithMessage("Choose how the money was paid.");
    }
}

public class RecordCustomerReturnCommandHandler : IRequestHandler<RecordCustomerReturnCommand, RecordCustomerReturnResult>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IAppSettings _settings;

    public RecordCustomerReturnCommandHandler(
        IAppDbContext db, ITenantContext tenant, ICurrentUserService currentUser, IAppSettings settings)
    {
        _db = db;
        _tenant = tenant;
        _currentUser = currentUser;
        _settings = settings;
    }

    public async Task<RecordCustomerReturnResult> Handle(RecordCustomerReturnCommand request, CancellationToken ct)
    {
        // Backdate window only (no period gate — a return doesn't belong to a billing period). The same
        // day governs the payment: they are one event, so they cannot report on different dates.
        BackdateGuard.Validate(request.ReturnedOn, _settings.BackdateWindowDays, "returnedOn");

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
                       ?? throw new NotFoundException("Customer", request.CustomerId);

        var productExists = await _db.Products.AnyAsync(p => p.Id == request.ProductId, ct);
        if (!productExists)
            throw new NotFoundException("Product", request.ProductId);

        // Get-or-create the product's inventory row and move the float (issued−, returned+), mirroring
        // AddInventoryMovement so the manual and delivery-driven paths never diverge.
        var inv = await _db.Inventories.FirstOrDefaultAsync(i => i.ProductId == request.ProductId, ct);
        if (inv is null)
        {
            inv = new Domain.Entities.Tenant.Inventory
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                ProductId = request.ProductId,
                LastUpdated = DateTime.UtcNow
            };
            _db.Inventories.Add(inv);
        }

        // A damaged jar leaves the customer's hands (issued−) AND is written off (damaged+); a good
        // one re-enters the reusable float. Same rule as AddInventoryMovement, so the two never diverge.
        var movementType = request.Damaged ? InventoryMovementType.Damage : InventoryMovementType.Return;
        InventoryMath.Apply(inv, movementType, request.Quantity, fromCustomer: true);

        var movement = new InventoryMovement
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ProductId = request.ProductId,
            OrderId = null,
            CustomerId = request.CustomerId,
            MovementType = movementType,
            Quantity = request.Quantity,
            PerformedBy = _currentUser.UserId,
            Notes = request.Notes,
            // Stamp the movement on the return day (midday, app zone) so the return history reports it on
            // that date; AppDbContext only auto-sets CreatedAt when it is left at default. Omitted → now.
            CreatedAt = request.ReturnedOn is { } on ? AppTimeZone.MiddayUtc(on) : default
        };
        _db.InventoryMovements.Add(movement);

        // The jars and the cash are one event at the counter, so they commit together. Saving them
        // separately is what produces the half-recorded counter visit: jars logged, money not, and the
        // customer chased for what he already handed over.
        var amount = request.CollectedAmount ?? 0m;
        Payment? payment = null;
        if (amount > 0m)
        {
            // This endpoint is gated on Inventory.Manage, which is a stock permission. Taking money
            // needs the money permission as well, or a stock-only role could book payments through the
            // returns door and never be visible on the collections screens they cannot open.
            if (!_currentUser.Permissions.Contains("Payments.Collect"))
                throw new ForbiddenAccessException();

            payment = PaymentRecording.Add(
                _db, _tenant, _currentUser, customer,
                amount, Enum.Parse<PaymentMethod>(request.PaymentMethod!),
                request.ReturnedOn, request.Notes);
        }

        // The in-memory test provider is non-relational and has no transactions (same guard as
        // CollectPayment). With no payment there is a single insert, so no transaction is needed.
        await using var tx = payment is not null && _db.IsRelational
            ? await _db.BeginTransactionAsync(ct)
            : null;

        await _db.SaveChangesAsync(ct);

        if (payment is not null)
        {
            // Spread it over the customer's oldest dues, exactly as Collect payment does — otherwise
            // the money is banked while the invoices still read unpaid until the nightly job catches up.
            await InvoiceAllocationSync.SyncAsync(_db, customer.Id, ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }

        return new RecordCustomerReturnResult(movement.Id, payment?.Id, amount);
    }
}
