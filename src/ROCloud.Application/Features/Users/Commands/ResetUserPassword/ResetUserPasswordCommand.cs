using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Users.Commands.ResetUserPassword;

/// <summary>Owner forces a password reset for a team member: sets a new temp password and emails it.</summary>
public sealed record ResetUserPasswordCommand(Guid Id) : IRequest;

public class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand>
{
    private const string PasswordAlphabet =
        "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789@#$%&*";

    private readonly IAppDbContext _db;
    private readonly IPasswordService _passwords;
    private readonly IEmailService _email;

    public ResetUserPasswordCommandHandler(IAppDbContext db, IPasswordService passwords, IEmailService email)
    {
        _db = db;
        _passwords = passwords;
        _email = email;
    }

    public async Task Handle(ResetUserPasswordCommand request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, ct)
                   ?? throw new NotFoundException("User", request.Id);

        var tempPassword = RandomNumberGenerator.GetString(PasswordAlphabet, 14);
        user.PasswordHash = _passwords.Hash(tempPassword);
        user.RefreshToken = null;               // force re-login everywhere
        user.RefreshTokenExpiresAt = null;
        await _db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(user.Email))
            await _email.SendAsync(
                user.Email,
                "Your ROCloud password was reset",
                $"Your password has been reset. Your new temporary password is: <b>{tempPassword}</b>. " +
                "Please change it after you sign in.", ct);
    }
}
