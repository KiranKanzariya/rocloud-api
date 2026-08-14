using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Security;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Application.Features.Users.Commands.ResetUserPassword;

/// <summary>
/// Owner forces a password reset for a team member: locks the current password and emails them a
/// single-use link to set a new one.
///
/// <para>This used to email a working temporary password. That is a standing credential sitting in an
/// inbox, and for a member who had never accepted their invitation it was a way straight past the
/// whole pending-invite gate — one click by the owner and a mistyped address received a live login.
/// A link cannot be used by anyone who does not hold the mailbox, and it expires.</para>
///
/// <para>An unaccepted member stays pending: the link they receive is still the invitation, and
/// accepting it is still what activates the account.</para>
/// </summary>
public sealed record ResetUserPasswordCommand(Guid Id) : IRequest;

public class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand>
{
    private const string PasswordAlphabet =
        "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789@#$%&*";

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPasswordService _passwords;
    private readonly IEmailService _email;
    private readonly ICacheService _cache;
    private readonly IAppSettings _settings;

    public ResetUserPasswordCommandHandler(
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

    public async Task Handle(ResetUserPasswordCommand request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, ct)
                   ?? throw new NotFoundException("User", request.Id);

        // Lock the old password immediately — a reset the owner asked for must take effect now, not
        // when the member gets round to reading the email. The replacement is unguessable and is
        // never sent anywhere; only the link can set a real one.
        user.PasswordHash = _passwords.Hash(RandomNumberGenerator.GetString(PasswordAlphabet, 14));
        user.RefreshToken = null;               // force re-login everywhere
        user.RefreshTokenExpiresAt = null;
        await UserSessions.RevokeAllAsync(_db, user.Id, ct);
        await _db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(user.Email))
            await UserInvitations.SendAsync(
                _db, _cache, _email, _settings, _tenant.TenantId, user.Id, user.Email, ct,
                // A member who never accepted is still being invited, whatever the owner called the button.
                isReset: user.InviteAcceptedAt is not null);
    }
}
