using FluentValidation;

namespace ROCloud.Application.Features.Auth.Commands.RegisterGoogle;

public class RegisterGoogleCommandValidator : AbstractValidator<RegisterGoogleCommand>
{
    private static readonly string[] PlanTypes = ["Basic", "Pro", "Enterprise"];

    public RegisterGoogleCommandValidator()
    {
        RuleFor(x => x.IdToken).NotEmpty();
        RuleFor(x => x.BusinessName).NotEmpty().Length(2, 200);
        // Mobile is optional for Google signup (Google does not supply one); validate only if given.
        RuleFor(x => x.Mobile)
            .Matches(@"^\+91[0-9]{10}$").When(x => !string.IsNullOrWhiteSpace(x.Mobile))
            .WithMessage("Invalid mobile number.");
        RuleFor(x => x.PlanType).Must(p => PlanTypes.Contains(p)).WithMessage("Invalid plan.");
        RuleFor(x => x.Subdomain)
            .Matches("^[a-z0-9-]{3,100}$").When(x => !string.IsNullOrEmpty(x.Subdomain))
            .WithMessage("Subdomain may contain only lowercase letters, numbers and hyphens.");
    }
}
