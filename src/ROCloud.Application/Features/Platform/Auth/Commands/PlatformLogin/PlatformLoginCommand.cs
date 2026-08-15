using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Features.Platform.Auth.Common;
using ROCloud.Application.Features.Platform.Auth.Services;

namespace ROCloud.Application.Features.Platform.Auth.Commands.PlatformLogin;

/// <summary>Platform staff login (super-admin portal). Authenticates against platform_users.</summary>
public sealed record PlatformLoginCommand(string Email, string Password) : IRequest<PlatformAuthResult>;

public class PlatformLoginCommandValidator : AbstractValidator<PlatformLoginCommand>
{
    public PlatformLoginCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress();
        RuleFor(c => c.Password).NotEmpty();
    }
}

public class PlatformLoginCommandHandler : IRequestHandler<PlatformLoginCommand, PlatformAuthResult>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordService _passwords;
    private readonly LoginAttemptService _attempts;
    private readonly PlatformTokenIssuer _issuer;

    public PlatformLoginCommandHandler(
        IAppDbContext db, IPasswordService passwords, LoginAttemptService attempts, PlatformTokenIssuer issuer)
    {
        _db = db;
        _passwords = passwords;
        _attempts = attempts;
        _issuer = issuer;
    }

    public async Task<PlatformAuthResult> Handle(PlatformLoginCommand request, CancellationToken ct)
    {
        var user = await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Email == request.Email, ct);

        // The lockout lives on the row now, so it can only be read once the account is loaded. It used
        // to be an in-memory counter keyed on the address, which every restart reset — on the sign-in
        // that reaches every workspace on the platform.
        if (user is not null && _attempts.IsLockedOut(user))
            throw new AccountLockedException(_attempts.LockoutMinutes);

        if (user is null || user.PasswordHash is null || !user.IsActive
            || !_passwords.Verify(request.Password, user.PasswordHash))
        {
            if (user is not null)
            {
                _attempts.RecordFailure(user);
                await _db.SaveChangesAsync(ct);
            }

            await Task.Delay(Random.Shared.Next(200, 400), ct);
            throw new InvalidCredentialsException();
        }

        // Persisted by the issuer's SaveChanges, in the same unit of work as the new session.
        _attempts.Clear(user);
        return await _issuer.IssueAsync(user, ct);
    }
}
