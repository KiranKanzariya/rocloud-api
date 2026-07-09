using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Auth.Commands.FindWorkspace;

public class FindWorkspaceCommandHandler : IRequestHandler<FindWorkspaceCommand>
{
    private readonly IAppDbContext _db;
    private readonly IEmailService _email;
    private readonly IAppSettings _settings;

    public FindWorkspaceCommandHandler(IAppDbContext db, IEmailService email, IAppSettings settings)
    {
        _db = db;
        _email = email;
        _settings = settings;
    }

    public async Task Handle(FindWorkspaceCommand request, CancellationToken ct)
    {
        // Anonymous endpoint — no tenant context, so bypass the tenant query filter. An email can
        // belong to users in several tenants (email is unique only per tenant).
        var tenantIds = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Email == request.Email && u.IsActive && !u.IsDeleted)
            .Select(u => u.TenantId)
            .Distinct()
            .ToListAsync(ct);

        if (tenantIds.Count > 0)
        {
            // Include Cancelled workspaces: their owner can sign in and re-subscribe to reactivate (guide §25).
            var tenants = await _db.Tenants.IgnoreQueryFilters()
                .Where(t => tenantIds.Contains(t.Id) && !t.IsDeleted)
                .OrderBy(t => t.Name)
                .Select(t => new { t.Name, t.Subdomain })
                .ToListAsync(ct);

            if (tenants.Count > 0)
            {
                var lines = string.Join("\n", tenants.Select(t =>
                    $"- {t.Name}: {_settings.TenantUrlFormat.Replace("{subdomain}", t.Subdomain)}"));

                await _email.SendAsync(
                    request.Email,
                    "Your ROCloud sign-in link" + (tenants.Count > 1 ? "s" : ""),
                    $"You can sign in to ROCloud here:\n\n{lines}\n\nBookmark your link so you can find it next time.",
                    ct);
            }
        }

        // Always succeed — never reveal whether the email exists.
    }
}
