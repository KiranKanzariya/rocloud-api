using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Features.Platform.Auth.Commands.PlatformForgotPassword;

namespace ROCloud.Application.Features.Platform.Auth.Commands.PlatformResetPassword;

/// <summary>Completes a platform password reset using the token emailed by PlatformForgotPassword.</summary>
public sealed record PlatformResetPasswordCommand(string Token, string NewPassword) : IRequest;

public class PlatformResetPasswordCommandValidator : AbstractValidator<PlatformResetPasswordCommand>
{
    public PlatformResetPasswordCommandValidator()
    {
        RuleFor(c => c.Token).NotEmpty();
        RuleFor(c => c.NewPassword).NotEmpty().MinimumLength(10);
    }
}

public class PlatformResetPasswordCommandHandler : IRequestHandler<PlatformResetPasswordCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICacheService _cache;
    private readonly IPasswordService _passwords;

    public PlatformResetPasswordCommandHandler(IAppDbContext db, ICacheService cache, IPasswordService passwords)
    {
        _db = db;
        _cache = cache;
        _passwords = passwords;
    }

    public async Task Handle(PlatformResetPasswordCommand request, CancellationToken ct)
    {
        var key = $"{PlatformForgotPasswordCommandHandler.CacheKeyPrefix}{request.Token}";
        var data = await _cache.GetAsync<PasswordResetToken>(key, ct)
                   ?? throw new InvalidCredentialsException();

        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == data.UserId, ct)
                   ?? throw new NotFoundException("PlatformUser", data.UserId);

        user.PasswordHash = _passwords.Hash(request.NewPassword);
        // Revoke existing sessions after a reset.
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;
        await _db.SaveChangesAsync(ct);

        await _cache.RemoveAsync(key, ct);
    }
}
