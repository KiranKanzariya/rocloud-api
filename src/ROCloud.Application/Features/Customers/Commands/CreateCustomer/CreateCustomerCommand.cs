using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Sanitisation;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Customers.Commands.CreateCustomer;

public sealed record CreateCustomerCommand(
    Guid? AreaId,
    string Name,
    string? Mobile,
    string? AlternateMobile,
    string? Email,
    string? AddressLine,
    string? Landmark,
    decimal? Latitude,
    decimal? Longitude,
    string DeliveryMode,
    string PaymentPreference,
    string? PreferredBottleSize,
    string? PreferredLanguage,
    [property: SanitizeHtml] string? Notes) : IRequest<Guid>;

public class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty().Length(2, 200)
            .Matches(@"^[\p{L}\p{N}\s.\-,']+$").WithMessage("Name contains invalid characters.");

        // Mobile is OPTIONAL — an owner may not have every customer's number (a shop, an old book
        // entry). Validate the format only when one is given; the mobile-less customer is then
        // identified by name, and mobile-only features (WhatsApp, SMS) simply skip them.
        RuleFor(c => c.Mobile)
            .Matches(@"^\+91[0-9]{10}$").When(c => !string.IsNullOrEmpty(c.Mobile))
            .WithMessage("Invalid mobile number.");

        RuleFor(c => c.AlternateMobile)
            .Matches(@"^\+91[0-9]{10}$").When(c => !string.IsNullOrEmpty(c.AlternateMobile))
            .WithMessage("Invalid alternate mobile number.");

        RuleFor(c => c.Email)
            .EmailAddress().MaximumLength(200).When(c => !string.IsNullOrEmpty(c.Email));

        RuleFor(c => c.AddressLine).MaximumLength(500);
        RuleFor(c => c.Landmark).MaximumLength(200);
        RuleFor(c => c.PreferredLanguage).MaximumLength(5);

        RuleFor(c => c.DeliveryMode)
            .Must(CustomerValidation.IsDeliveryMode).WithMessage("Invalid delivery mode.");
        RuleFor(c => c.PaymentPreference)
            .Must(CustomerValidation.IsPaymentPreference).WithMessage("Invalid payment preference.");
        RuleFor(c => c.PreferredBottleSize)
            .Must(s => CustomerValidation.IsBottleSize(s)).When(c => !string.IsNullOrEmpty(c.PreferredBottleSize))
            .WithMessage("Invalid bottle size.");
    }
}

public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public CreateCustomerCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(CreateCustomerCommand request, CancellationToken ct)
    {
        await Subscription.PlanLimits.EnsureCanAddCustomerAsync(_db, _tenant, ct);

        if (!string.IsNullOrWhiteSpace(request.Mobile))
        {
            var mobileTaken = await _db.Customers.AnyAsync(c => c.Mobile == request.Mobile, ct);
            if (mobileTaken)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["mobile"] = ["A customer with this mobile number already exists."]
                });
        }

        // Sequential per-tenant code (includes soft-deleted so codes are not reused).
        var existingCount = await _db.Customers.IgnoreQueryFilters()
            .CountAsync(c => c.TenantId == _tenant.TenantId, ct);
        var customerCode = $"CUST-{existingCount + 1:D5}";

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            AreaId = request.AreaId,
            CustomerCode = customerCode,
            Name = request.Name,
            Mobile = string.IsNullOrWhiteSpace(request.Mobile) ? null : request.Mobile,
            AlternateMobile = request.AlternateMobile,
            Email = request.Email,
            AddressLine = request.AddressLine,
            Landmark = request.Landmark,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            DeliveryMode = Enum.Parse<DeliveryMode>(request.DeliveryMode),
            PaymentPreference = Enum.Parse<PaymentPreference>(request.PaymentPreference),
            PreferredBottleSize = BottleSizeExtensions.FromWire(request.PreferredBottleSize),
            PreferredLanguage = request.PreferredLanguage,
            Notes = request.Notes,
            IsActive = true
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);
        return customer.Id;
    }
}
