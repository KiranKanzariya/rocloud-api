using FluentValidation;
using MediatR;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Features.Areas.Commands.CreateArea;

/// <summary>Creates a delivery area for the current tenant.</summary>
public sealed record CreateAreaCommand(
    string Name,
    string? City,
    string? Pincode) : IRequest<Guid>;

public class CreateAreaCommandValidator : AbstractValidator<CreateAreaCommand>
{
    public CreateAreaCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().Length(2, 100);
        RuleFor(c => c.City).MaximumLength(100);
        RuleFor(c => c.Pincode)
            .Matches(@"^[0-9]{6}$").When(c => !string.IsNullOrEmpty(c.Pincode))
            .WithMessage("Pincode must be 6 digits.");
    }
}

public class CreateAreaCommandHandler : IRequestHandler<CreateAreaCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public CreateAreaCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(CreateAreaCommand request, CancellationToken ct)
    {
        var area = new Area
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            Name = request.Name,
            City = request.City,
            Pincode = request.Pincode,
            IsActive = true
        };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync(ct);
        return area.Id;
    }
}
