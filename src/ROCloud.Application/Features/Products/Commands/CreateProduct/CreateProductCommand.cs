using FluentValidation;
using MediatR;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Products.Commands.CreateProduct;

public sealed record CreateProductCommand(
    string Name,
    string BottleSize,
    decimal DefaultRate,
    string? Unit) : IRequest<Guid>;

public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().Length(1, 200);
        RuleFor(c => c.BottleSize)
            .Must(v => BottleSizeExtensions.FromWire(v) is not null)
            .WithMessage("Invalid bottle size. Allowed: 18L, 20L, 250ml, 500ml, 1L, Custom.");
        RuleFor(c => c.DefaultRate).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Unit).MaximumLength(20);
    }
}

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public CreateProductCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            Name = request.Name,
            BottleSize = BottleSizeExtensions.FromWire(request.BottleSize)!.Value,
            DefaultRate = request.DefaultRate,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "bottle" : request.Unit,
            IsActive = true
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);
        return product.Id;
    }
}
