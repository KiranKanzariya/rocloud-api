using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Products.Commands.UpdateProduct;

public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    string BottleSize,
    decimal DefaultRate,
    string? Unit,
    string? Hsn,
    bool IsActive) : IRequest;

public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().Length(1, 200);
        RuleFor(c => c.BottleSize)
            .Must(v => BottleSizeExtensions.FromWire(v) is not null)
            .WithMessage("Invalid bottle size. Allowed: 18L, 20L, 250ml, 500ml, 1L, Custom.");
        RuleFor(c => c.DefaultRate).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Unit).MaximumLength(20);
        RuleFor(c => c.Hsn).MaximumLength(8);
    }
}

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand>
{
    private readonly IAppDbContext _db;

    public UpdateProductCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(UpdateProductCommand request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.Id, ct)
                      ?? throw new NotFoundException("Product", request.Id);

        product.Name = request.Name;
        product.BottleSize = BottleSizeExtensions.FromWire(request.BottleSize)!.Value;
        product.DefaultRate = request.DefaultRate;
        product.Unit = string.IsNullOrWhiteSpace(request.Unit) ? "bottle" : request.Unit;
        product.Hsn = string.IsNullOrWhiteSpace(request.Hsn) ? null : request.Hsn.Trim();
        product.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
    }
}
