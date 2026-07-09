using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Services;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Inventory.Commands.AddInventoryMovement;

/// <summary>
/// Manual stock entry (Issue / Return / Damage / Restock / Adjustment). Updates the parent
/// inventory row's counters and records a movement. Adjustment may be negative; all other
/// types require a positive quantity.
/// </summary>
public sealed record AddInventoryMovementCommand(
    Guid ProductId,
    string MovementType,
    int Quantity,
    Guid? OrderId,
    Guid? CustomerId,
    string? Notes) : IRequest<Guid>;

public class AddInventoryMovementCommandValidator : AbstractValidator<AddInventoryMovementCommand>
{
    public AddInventoryMovementCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.MovementType)
            .Must(v => Enum.GetNames<InventoryMovementType>().Contains(v))
            .WithMessage("Invalid movement type.");
        RuleFor(c => c.Quantity)
            .NotEqual(0).WithMessage("Quantity cannot be zero.");
        // Only Adjustment may be negative.
        RuleFor(c => c.Quantity)
            .GreaterThan(0)
            .When(c => c.MovementType != nameof(InventoryMovementType.Adjustment))
            .WithMessage("Quantity must be positive for this movement type.");
        RuleFor(c => c.Notes).MaximumLength(1000);
    }
}

public class AddInventoryMovementCommandHandler : IRequestHandler<AddInventoryMovementCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserService _currentUser;

    public AddInventoryMovementCommandHandler(
        IAppDbContext db, ITenantContext tenant, ICurrentUserService currentUser)
    {
        _db = db;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    public async Task<Guid> Handle(AddInventoryMovementCommand request, CancellationToken ct)
    {
        var productExists = await _db.Products.AnyAsync(p => p.Id == request.ProductId, ct);
        if (!productExists)
            throw new NotFoundException("Product", request.ProductId);

        var type = Enum.Parse<InventoryMovementType>(request.MovementType);

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

        // A Damage that carries a CustomerId is a jar returned broken — it also frees an issued jar.
        InventoryMath.Apply(inv, type, request.Quantity, fromCustomer: request.CustomerId is not null);

        var movement = new InventoryMovement
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            ProductId = request.ProductId,
            OrderId = request.OrderId,
            CustomerId = request.CustomerId,
            MovementType = type,
            Quantity = request.Quantity,
            PerformedBy = _currentUser.UserId,
            Notes = request.Notes
        };
        _db.InventoryMovements.Add(movement);

        await _db.SaveChangesAsync(ct);
        return movement.Id;
    }
}
