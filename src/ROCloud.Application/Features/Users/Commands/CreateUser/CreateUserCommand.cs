using FluentValidation;
using MediatR;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Application.Features.Users.Commands.CreateUser;

/// <summary>
/// Creates a team member and emails them an invitation to set their password.
///
/// <para>This used to email a temporary password, which made the account usable the instant it was
/// saved — so a mistyped address handed a working login to whoever received it. It now provisions the
/// same PENDING account as InviteUser and sends the same link; the two differ only in that this one
/// also carries a preferred language.</para>
/// </summary>
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
    private readonly ICacheService _cache;
    private readonly IAppSettings _settings;

    public CreateUserCommandHandler(
        IAppDbContext db, ITenantContext tenant, IPasswordService passwords, IEmailService email,
        ICacheService cache, IAppSettings settings)
    {
        _db = db;
        _tenant = tenant;
        _passwords = passwords;
        _email = email;
        _cache = cache;
        _settings = settings;
    }

    public async Task<Guid> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var user = await UserProvisioning.CreateAsync(
            _db, _tenant, _passwords,
            request.Name, request.Mobile, request.Email, request.RoleId,
            request.PreferredLanguage, request.AreaIds, ct);

        await _db.SaveChangesAsync(ct);

        await UserInvitations.SendAsync(
            _db, _cache, _email, _settings, _tenant.TenantId, user.Id, request.Email, ct);

        return user.Id;
    }
}
