using ROCloud.Application.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.CustomerSubscriptions.Commands.CreateCustomerSubscription;

/// <summary>
/// Creates a recurring delivery subscription for a customer (guide §9). The nightly rollover job turns
/// active subscriptions into next-day orders based on their frequency. Rate defaults to the product's
/// default rate and the start date to today when not supplied.
/// </summary>
public sealed record CreateCustomerSubscriptionCommand(
    Guid CustomerId,
    Guid ProductId,
    int Quantity,
    string Frequency,
    decimal? RatePerUnit,
    DateOnly? StartDate) : IRequest<Guid>;

public class CreateCustomerSubscriptionCommandValidator : AbstractValidator<CreateCustomerSubscriptionCommand>
{
    public CreateCustomerSubscriptionCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Quantity).GreaterThan(0);
        RuleFor(c => c.Frequency)
            .Must(v => Enum.GetNames<SubscriptionFrequency>().Contains(v))
            .WithMessage("Invalid subscription frequency.");
        RuleFor(c => c.RatePerUnit).GreaterThanOrEqualTo(0m).When(c => c.RatePerUnit.HasValue);
    }
}

public class CreateCustomerSubscriptionCommandHandler
    : IRequestHandler<CreateCustomerSubscriptionCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public CreateCustomerSubscriptionCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(CreateCustomerSubscriptionCommand request, CancellationToken ct)
    {
        var customerExists = await _db.Customers.AnyAsync(c => c.Id == request.CustomerId, ct);
        if (!customerExists)
            throw new NotFoundException("Customer", request.CustomerId);

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["productId"] = ["Unknown product."]
            });

        var subscription = new CustomerSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            CustomerId = request.CustomerId,
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            Frequency = Enum.Parse<SubscriptionFrequency>(request.Frequency),
            RatePerUnit = request.RatePerUnit ?? product.DefaultRate,
            StartDate = request.StartDate ?? AppTimeZone.Today(DateTime.UtcNow),
            IsActive = true
        };

        _db.CustomerSubscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);
        return subscription.Id;
    }
}
