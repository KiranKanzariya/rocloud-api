using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;

namespace ROCloud.Application.Features.Auth.Commands.ResetPassword;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICacheService _cache;
    private readonly IPasswordService _passwords;

    public ResetPasswordCommandHandler(IAppDbContext db, ICacheService cache, IPasswordService passwords)
    {
        _db = db;
        _cache = cache;
        _passwords = passwords;
    }

    public async Task Handle(ResetPasswordCommand request, CancellationToken ct)
    {
        var key = $"pwreset:{request.Token}";
        var data = await _cache.GetAsync<PasswordResetToken>(key, ct)
                   ?? throw new InvalidCredentialsException();

        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == data.UserId && !u.IsDeleted, ct)
            ?? throw new NotFoundException("User", data.UserId);

        user.PasswordHash = _passwords.Hash(request.NewPassword);
        // Revoke all existing sessions after a password reset.
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;
        await _db.SaveChangesAsync(ct);

        await _cache.RemoveAsync(key, ct);
    }
}
