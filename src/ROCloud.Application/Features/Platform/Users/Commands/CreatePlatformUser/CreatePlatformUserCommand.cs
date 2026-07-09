using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Platform;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Platform.Users.Commands.CreatePlatformUser;

/// <summary>Creates a platform staff member (guide §26). SuperAdmin only.</summary>
public sealed record CreatePlatformUserCommand(
    string Name, string Email, string Password, string PlatformRole) : IRequest<Guid>;

public class CreatePlatformUserCommandValidator : AbstractValidator<CreatePlatformUserCommand>
{
    private static readonly string[] Roles = ["SuperAdmin", "Support", "Finance"];

    public CreatePlatformUserCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().Length(2, 200);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(c => c.Password).NotEmpty().MinimumLength(10);
        RuleFor(c => c.PlatformRole).Must(v => Roles.Contains(v)).WithMessage("Invalid platform role.");
    }
}

public class CreatePlatformUserCommandHandler : IRequestHandler<CreatePlatformUserCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordService _passwords;

    public CreatePlatformUserCommandHandler(IAppDbContext db, IPasswordService passwords)
    {
        _db = db;
        _passwords = passwords;
    }

    public async Task<Guid> Handle(CreatePlatformUserCommand request, CancellationToken ct)
    {
        var exists = await _db.PlatformUsers.AnyAsync(u => u.Email == request.Email, ct);
        if (exists)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["email"] = ["A platform user with this email already exists."]
            });

        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Email = request.Email,
            PasswordHash = _passwords.Hash(request.Password),
            PlatformRole = request.PlatformRole,
            IsActive = true
        };
        _db.PlatformUsers.Add(user);
        await _db.SaveChangesAsync(ct);
        return user.Id;
    }
}
