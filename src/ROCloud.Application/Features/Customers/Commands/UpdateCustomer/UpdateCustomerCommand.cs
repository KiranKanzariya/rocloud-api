using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Sanitisation;
using ROCloud.Application.Features.Customers.Commands;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Customers.Commands.UpdateCustomer;

public sealed record UpdateCustomerCommand(
    Guid Id,
    Guid? AreaId,
    string Name,
    string Mobile,
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
    [property: SanitizeHtml] string? Notes,
    bool IsActive) : IRequest;

public class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Name)
            .NotEmpty().Length(2, 200)
            .Matches(@"^[\p{L}\p{N}\s.\-,']+$").WithMessage("Name contains invalid characters.");
        RuleFor(c => c.Mobile)
            .NotEmpty().Matches(@"^\+91[0-9]{10}$").WithMessage("Invalid mobile number.");
        RuleFor(c => c.Email)
            .EmailAddress().MaximumLength(200).When(c => !string.IsNullOrEmpty(c.Email));
        RuleFor(c => c.AddressLine).MaximumLength(500);
        RuleFor(c => c.DeliveryMode).Must(CustomerValidation.IsDeliveryMode).WithMessage("Invalid delivery mode.");
        RuleFor(c => c.PaymentPreference).Must(CustomerValidation.IsPaymentPreference).WithMessage("Invalid payment preference.");
        RuleFor(c => c.PreferredBottleSize)
            .Must(s => CustomerValidation.IsBottleSize(s)).When(c => !string.IsNullOrEmpty(c.PreferredBottleSize))
            .WithMessage("Invalid bottle size.");
    }
}

public class UpdateCustomerCommandHandler : IRequestHandler<UpdateCustomerCommand>
{
    private readonly IAppDbContext _db;

    public UpdateCustomerCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(UpdateCustomerCommand request, CancellationToken ct)
    {
        // Tenant query filter + explicit id → cross-tenant access yields NotFound (404).
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                       ?? throw new NotFoundException("Customer", request.Id);

        if (customer.Mobile != request.Mobile)
        {
            var mobileTaken = await _db.Customers.AnyAsync(c => c.Mobile == request.Mobile && c.Id != request.Id, ct);
            if (mobileTaken)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["mobile"] = ["A customer with this mobile number already exists."]
                });
        }

        customer.AreaId = request.AreaId;
        customer.Name = request.Name;
        customer.Mobile = request.Mobile;
        customer.AlternateMobile = request.AlternateMobile;
        customer.Email = request.Email;
        customer.AddressLine = request.AddressLine;
        customer.Landmark = request.Landmark;
        customer.Latitude = request.Latitude;
        customer.Longitude = request.Longitude;
        customer.DeliveryMode = Enum.Parse<DeliveryMode>(request.DeliveryMode);
        customer.PaymentPreference = Enum.Parse<PaymentPreference>(request.PaymentPreference);
        customer.PreferredBottleSize = BottleSizeExtensions.FromWire(request.PreferredBottleSize);
        customer.PreferredLanguage = request.PreferredLanguage;
        customer.Notes = request.Notes;
        customer.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
    }
}
