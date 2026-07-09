using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.CustomerSubscriptions.Commands.UpdateCustomerSubscription;

/// <summary>
/// Edits an existing delivery subscription in place — quantity, frequency and rate. The product is not
/// changed (switch product = end this one and add a new one). The nightly rollover job picks up the new
/// settings from the next run onward; orders already generated for upcoming dates are not retro-changed.
/// </summary>
public sealed record UpdateCustomerSubscriptionCommand(
    Guid Id,
    Guid CustomerId,
    int Quantity,
    string Frequency,
    decimal? RatePerUnit) : IRequest;

public class UpdateCustomerSubscriptionCommandValidator : AbstractValidator<UpdateCustomerSubscriptionCommand>
{
    public UpdateCustomerSubscriptionCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.Quantity).GreaterThan(0);
        RuleFor(c => c.Frequency)
            .Must(v => Enum.GetNames<SubscriptionFrequency>().Contains(v))
            .WithMessage("Invalid subscription frequency.");
        RuleFor(c => c.RatePerUnit).GreaterThanOrEqualTo(0m).When(c => c.RatePerUnit.HasValue);
    }
}

public class UpdateCustomerSubscriptionCommandHandler : IRequestHandler<UpdateCustomerSubscriptionCommand>
{
    private readonly IAppDbContext _db;

    public UpdateCustomerSubscriptionCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(UpdateCustomerSubscriptionCommand request, CancellationToken ct)
    {
        var subscription = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.Id == request.Id && s.CustomerId == request.CustomerId, ct)
            ?? throw new NotFoundException("CustomerSubscription", request.Id);

        subscription.Quantity = request.Quantity;
        subscription.Frequency = Enum.Parse<SubscriptionFrequency>(request.Frequency);
        if (request.RatePerUnit is { } rate)
            subscription.RatePerUnit = rate;

        await _db.SaveChangesAsync(ct);
    }
}
