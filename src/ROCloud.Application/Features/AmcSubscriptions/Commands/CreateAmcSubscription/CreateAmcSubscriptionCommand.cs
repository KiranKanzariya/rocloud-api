using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Features.AmcSubscriptions.Commands.CreateAmcSubscription;

/// <summary>
/// Creates an AMC subscription for a customer. NextDueDate defaults to StartDate + interval
/// (first routine visit one interval after the contract starts) unless explicitly supplied.
/// </summary>
public sealed record CreateAmcSubscriptionCommand(
    Guid CustomerId,
    string? PlanName,
    int IntervalMonths,
    decimal Amount,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? FirstDueDate) : IRequest<Guid>;

public class CreateAmcSubscriptionCommandValidator : AbstractValidator<CreateAmcSubscriptionCommand>
{
    private static readonly int[] AllowedIntervals = [3, 6, 12];

    public CreateAmcSubscriptionCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.PlanName).MaximumLength(100);
        RuleFor(c => c.IntervalMonths)
            .Must(v => AllowedIntervals.Contains(v))
            .WithMessage("Interval must be 3, 6, or 12 months.");
        RuleFor(c => c.Amount).GreaterThanOrEqualTo(0m);
        RuleFor(c => c.EndDate)
            .GreaterThanOrEqualTo(c => c.StartDate).When(c => c.EndDate.HasValue)
            .WithMessage("EndDate must be on or after StartDate.");
    }
}

public class CreateAmcSubscriptionCommandHandler : IRequestHandler<CreateAmcSubscriptionCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public CreateAmcSubscriptionCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(CreateAmcSubscriptionCommand request, CancellationToken ct)
    {
        var customerExists = await _db.Customers.AnyAsync(c => c.Id == request.CustomerId, ct);
        if (!customerExists)
            throw new NotFoundException("Customer", request.CustomerId);

        var subscription = new AmcSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            CustomerId = request.CustomerId,
            PlanName = request.PlanName,
            IntervalMonths = request.IntervalMonths,
            Amount = request.Amount,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            NextDueDate = request.FirstDueDate ?? request.StartDate.AddMonths(request.IntervalMonths),
            IsActive = true
        };

        _db.AmcSubscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);
        return subscription.Id;
    }
}
