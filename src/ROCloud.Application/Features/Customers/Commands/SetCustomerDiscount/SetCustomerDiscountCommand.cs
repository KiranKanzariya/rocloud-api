using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Customers.Commands.SetCustomerDiscount;

/// <summary>
/// Sets a customer's standing (recurring) discount on their water invoices (guide §26). Set by the
/// tenant's own staff (Customers.Edit). Percentage 0–100, Fixed ≥ 0, or None to clear; applied
/// automatically to future invoices for the customer.
/// </summary>
public sealed record SetCustomerDiscountCommand(
    Guid Id, string DiscountType, decimal DiscountValue) : IRequest;

public class SetCustomerDiscountCommandValidator : AbstractValidator<SetCustomerDiscountCommand>
{
    public SetCustomerDiscountCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.DiscountType)
            .Must(v => Enum.TryParse<CustomerDiscountType>(v, out _))
            .WithMessage("Discount type must be None, Percentage, or Fixed.");
        RuleFor(c => c.DiscountValue).GreaterThanOrEqualTo(0m);
        RuleFor(c => c.DiscountValue).LessThanOrEqualTo(100m)
            .When(c => string.Equals(c.DiscountType, "Percentage", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Percentage discount must be between 0 and 100.");
    }
}

public class SetCustomerDiscountCommandHandler : IRequestHandler<SetCustomerDiscountCommand>
{
    private readonly IAppDbContext _db;

    public SetCustomerDiscountCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(SetCustomerDiscountCommand request, CancellationToken ct)
    {
        // Tenant query filter + explicit id → cross-tenant access yields NotFound (404).
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                       ?? throw new NotFoundException("Customer", request.Id);

        var type = Enum.Parse<CustomerDiscountType>(request.DiscountType);
        customer.DiscountType = type;
        customer.DiscountValue = type == CustomerDiscountType.None ? 0m : request.DiscountValue;

        await _db.SaveChangesAsync(ct);
    }
}
