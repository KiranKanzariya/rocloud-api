using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Platform.Users.Commands.UpdatePlatformUser;

/// <summary>Updates a platform staff member's name, role, and active flag (guide §26). SuperAdmin only.</summary>
public sealed record UpdatePlatformUserCommand(
    Guid Id, string Name, string PlatformRole, bool IsActive) : IRequest;

public class UpdatePlatformUserCommandValidator : AbstractValidator<UpdatePlatformUserCommand>
{
    private static readonly string[] Roles = ["SuperAdmin", "Support", "Finance"];

    public UpdatePlatformUserCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().Length(2, 200);
        RuleFor(c => c.PlatformRole).Must(v => Roles.Contains(v)).WithMessage("Invalid platform role.");
    }
}

public class UpdatePlatformUserCommandHandler : IRequestHandler<UpdatePlatformUserCommand>
{
    private readonly IAppDbContext _db;

    public UpdatePlatformUserCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(UpdatePlatformUserCommand request, CancellationToken ct)
    {
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == request.Id, ct)
                   ?? throw new NotFoundException("PlatformUser", request.Id);

        user.Name = request.Name;
        user.PlatformRole = request.PlatformRole;
        user.IsActive = request.IsActive;
        // Deactivation revokes any active session.
        if (!request.IsActive)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
        }
        await _db.SaveChangesAsync(ct);
    }
}
