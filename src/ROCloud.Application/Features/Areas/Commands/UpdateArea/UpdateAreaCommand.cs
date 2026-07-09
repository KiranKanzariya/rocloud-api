using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Areas.Commands.UpdateArea;

/// <summary>Updates a delivery area (name, city, pincode, active flag).</summary>
public sealed record UpdateAreaCommand(
    Guid Id,
    string Name,
    string? City,
    string? Pincode,
    bool IsActive) : IRequest;

public class UpdateAreaCommandValidator : AbstractValidator<UpdateAreaCommand>
{
    public UpdateAreaCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().Length(2, 100);
        RuleFor(c => c.City).MaximumLength(100);
        RuleFor(c => c.Pincode)
            .Matches(@"^[0-9]{6}$").When(c => !string.IsNullOrEmpty(c.Pincode))
            .WithMessage("Pincode must be 6 digits.");
    }
}

public class UpdateAreaCommandHandler : IRequestHandler<UpdateAreaCommand>
{
    private readonly IAppDbContext _db;

    public UpdateAreaCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(UpdateAreaCommand request, CancellationToken ct)
    {
        var area = await _db.Areas.FirstOrDefaultAsync(a => a.Id == request.Id, ct)
                   ?? throw new NotFoundException("Area", request.Id);

        area.Name = request.Name;
        area.City = request.City;
        area.Pincode = request.Pincode;
        area.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
    }
}
