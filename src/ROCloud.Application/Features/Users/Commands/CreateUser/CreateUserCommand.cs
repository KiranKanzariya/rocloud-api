using FluentValidation;
using MediatR;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Users.Commands.CreateUser;

/// <summary>Creates a team member with a temporary password (emailed to them).</summary>
public sealed record CreateUserCommand(
    string Name,
    string Email,
    string? Mobile,
    Guid RoleId,
    string? PreferredLanguage,
    IReadOnlyList<Guid>? AreaIds) : IRequest<Guid>;

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().Length(2, 200);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(c => c.Mobile)
            .Matches(@"^\+91[0-9]{10}$").When(c => !string.IsNullOrEmpty(c.Mobile))
            .WithMessage("Invalid mobile number.");
        RuleFor(c => c.RoleId).NotEmpty();
        RuleFor(c => c.PreferredLanguage).MaximumLength(5);
    }
}

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPasswordService _passwords;
    private readonly IEmailService _email;

    public CreateUserCommandHandler(
        IAppDbContext db, ITenantContext tenant, IPasswordService passwords, IEmailService email)
    {
        _db = db;
        _tenant = tenant;
        _passwords = passwords;
        _email = email;
    }

    public async Task<Guid> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var (user, tempPassword) = await UserProvisioning.CreateAsync(
            _db, _tenant, _passwords,
            request.Name, request.Mobile, request.Email, request.RoleId,
            request.PreferredLanguage, request.AreaIds, ct);

        await _db.SaveChangesAsync(ct);

        await _email.SendAsync(
            request.Email,
            "Your ROCloud account",
            $"An account has been created for you. Sign in with your email and this temporary " +
            $"password: <b>{tempPassword}</b>. Please change it after your first login.", ct);

        return user.Id;
    }
}
