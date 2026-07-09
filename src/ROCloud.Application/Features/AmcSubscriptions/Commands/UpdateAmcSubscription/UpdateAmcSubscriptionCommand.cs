using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.AmcSubscriptions.Commands.UpdateAmcSubscription;

public sealed record UpdateAmcSubscriptionCommand(
    Guid Id,
    string? PlanName,
    int IntervalMonths,
    decimal Amount,
    DateOnly? EndDate,
    DateOnly NextDueDate,
    DateOnly? LastServiceDate,
    bool IsActive) : IRequest;

public class UpdateAmcSubscriptionCommandValidator : AbstractValidator<UpdateAmcSubscriptionCommand>
{
    private static readonly int[] AllowedIntervals = [3, 6, 12];

    public UpdateAmcSubscriptionCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.PlanName).MaximumLength(100);
        RuleFor(c => c.IntervalMonths)
            .Must(v => AllowedIntervals.Contains(v))
            .WithMessage("Interval must be 3, 6, or 12 months.");
        RuleFor(c => c.Amount).GreaterThanOrEqualTo(0m);
    }
}

public class UpdateAmcSubscriptionCommandHandler : IRequestHandler<UpdateAmcSubscriptionCommand>
{
    private readonly IAppDbContext _db;

    public UpdateAmcSubscriptionCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(UpdateAmcSubscriptionCommand request, CancellationToken ct)
    {
        var subscription = await _db.AmcSubscriptions.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
                           ?? throw new NotFoundException("AmcSubscription", request.Id);

        subscription.PlanName = request.PlanName;
        subscription.IntervalMonths = request.IntervalMonths;
        subscription.Amount = request.Amount;
        subscription.EndDate = request.EndDate;
        subscription.NextDueDate = request.NextDueDate;
        subscription.LastServiceDate = request.LastServiceDate;
        subscription.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
    }
}
