using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Platform;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Platform.Auth.Commands.BootstrapAdmin;

/// <summary>
/// First-run only: creates the initial SuperAdmin when no platform users exist yet (guide §26).
/// Self-disables once any platform user is present, so it is safe to leave exposed.
/// </summary>
public sealed record BootstrapAdminCommand(string Name, string Email, string Password) : IRequest<Guid>;

public class BootstrapAdminCommandValidator : AbstractValidator<BootstrapAdminCommand>
{
    public BootstrapAdminCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().Length(2, 200);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(c => c.Password).NotEmpty().MinimumLength(10);
    }
}

public class BootstrapAdminCommandHandler : IRequestHandler<BootstrapAdminCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordService _passwords;

    public BootstrapAdminCommandHandler(IAppDbContext db, IPasswordService passwords)
    {
        _db = db;
        _passwords = passwords;
    }

    public async Task<Guid> Handle(BootstrapAdminCommand request, CancellationToken ct)
    {
        if (await _db.PlatformUsers.AnyAsync(ct))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["bootstrap"] = ["Platform admin already initialised."]
            });

        var admin = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Email = request.Email,
            PasswordHash = _passwords.Hash(request.Password),
            PlatformRole = "SuperAdmin",
            IsActive = true
        };
        _db.PlatformUsers.Add(admin);
        await _db.SaveChangesAsync(ct);
        return admin.Id;
    }
}
