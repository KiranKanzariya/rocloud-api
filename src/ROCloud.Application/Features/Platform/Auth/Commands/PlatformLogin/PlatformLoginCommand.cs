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
        var clientId = $"platform:{request.Email}".ToLowerInvariant();

        if (await _attempts.IsLockedOutAsync(clientId, ct))
            throw new AccountLockedException(_attempts.LockoutMinutes);

        var user = await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Email == request.Email, ct);

        if (user is null || user.PasswordHash is null || !user.IsActive
            || !_passwords.Verify(request.Password, user.PasswordHash))
        {
            await _attempts.RecordFailureAsync(clientId, ct);
            await Task.Delay(Random.Shared.Next(200, 400), ct);
            throw new InvalidCredentialsException();
        }

        await _attempts.ClearAsync(clientId, ct);
        return await _issuer.IssueAsync(user, ct);
    }
}
