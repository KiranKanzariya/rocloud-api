using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
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
    string? Notes) : IRequest<Guid>;

public class RecordCustomerReturnCommandValidator : AbstractValidator<RecordCustomerReturnCommand>
{
    public RecordCustomerReturnCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Quantity).GreaterThan(0);
        RuleFor(c => c.Notes).MaximumLength(1000);
    }
}

public class RecordCustomerReturnCommandHandler : IRequestHandler<RecordCustomerReturnCommand, Guid>
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

    public async Task<Guid> Handle(RecordCustomerReturnCommand request, CancellationToken ct)
    {
        // Backdate window only (no period gate — a return doesn't belong to a billing period).
        BackdateGuard.Validate(request.ReturnedOn, _settings.BackdateWindowDays, "returnedOn");

        var customerExists = await _db.Customers.AnyAsync(c => c.Id == request.CustomerId, ct);
        if (!customerExists)
            throw new NotFoundException("Customer", request.CustomerId);

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

        InventoryMath.Apply(inv, InventoryMovementType.Return, request.Quantity, fromCustomer: true);

        var movement = new InventoryMovement
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ProductId = request.ProductId,
            OrderId = null,
            CustomerId = request.CustomerId,
            MovementType = InventoryMovementType.Return,
            Quantity = request.Quantity,
            PerformedBy = _currentUser.UserId,
            Notes = request.Notes,
            // Stamp the movement on the return day (midday, app zone) so the return history reports it on
            // that date; AppDbContext only auto-sets CreatedAt when it is left at default. Omitted → now.
            CreatedAt = request.ReturnedOn is { } on ? AppTimeZone.MiddayUtc(on) : default
        };
        _db.InventoryMovements.Add(movement);

        await _db.SaveChangesAsync(ct);
        return movement.Id;
    }
}
