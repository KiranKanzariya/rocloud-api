using FluentValidation;
using ROCloud.Application.Common.Validation;

namespace ROCloud.Application.Features.Auth.Commands.Register;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    private static readonly string[] PlanTypes = ["Basic", "Pro", "Enterprise"];

    public RegisterCommandValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().Length(2, 200);
        RuleFor(x => x.OwnerName).NotEmpty().Length(2, 200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Password).Password();
        RuleFor(x => x.Mobile).NotEmpty().Matches(@"^\+91[0-9]{10}$").WithMessage("Invalid mobile number.");
        RuleFor(x => x.PlanType).Must(p => PlanTypes.Contains(p)).WithMessage("Invalid plan.");
        RuleFor(x => x.Subdomain)
            .Matches("^[a-z0-9-]{3,100}$").When(x => !string.IsNullOrEmpty(x.Subdomain))
            .WithMessage("Subdomain may contain only lowercase letters, numbers and hyphens.");
    }
}
