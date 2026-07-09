using FluentValidation;
using MediatR;

namespace ROCloud.Application.Features.Auth.Commands.FindWorkspace;

/// <summary>
/// "Forgot your workspace?" recovery — emails the caller every ROCloud portal (subdomain) their
/// email can sign in to. Always succeeds; never reveals whether the email exists (anti-enumeration,
/// same convention as <see cref="ForgotPassword.ForgotPasswordCommand"/>).
/// </summary>
public sealed record FindWorkspaceCommand(string Email) : IRequest;

public class FindWorkspaceCommandValidator : AbstractValidator<FindWorkspaceCommand>
{
    public FindWorkspaceCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}
