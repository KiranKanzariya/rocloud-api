using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;

namespace ROCloud.Application.Features.Auth.Commands.GoogleWorkspaces;

/// <summary>
/// Apex-domain step of Google sign-in (guide §5). Verifies a Google id-token and returns every
/// workspace that has an active user for that Google identity, each with a short-lived handoff URL.
/// Tenant-agnostic: runs on the central app domain (a single Google "Authorized origin"), so we
/// never need to register every tenant subdomain with Google.
/// </summary>
public sealed record ResolveGoogleWorkspacesCommand(string IdToken) : IRequest<IReadOnlyList<GoogleWorkspaceDto>>;

public class ResolveGoogleWorkspacesCommandHandler
    : IRequestHandler<ResolveGoogleWorkspacesCommand, IReadOnlyList<GoogleWorkspaceDto>>
{
    private readonly IAppDbContext _db;
    private readonly IGoogleAuthService _google;
    private readonly ITokenService _tokens;
    private readonly IAppSettings _settings;

    public ResolveGoogleWorkspacesCommandHandler(
        IAppDbContext db, IGoogleAuthService google, ITokenService tokens, IAppSettings settings)
    {
        _db = db;
        _google = google;
        _tokens = tokens;
        _settings = settings;
    }

    public async Task<IReadOnlyList<GoogleWorkspaceDto>> Handle(
        ResolveGoogleWorkspacesCommand request, CancellationToken ct)
    {
        var info = await _google.ValidateAsync(request.IdToken, ct);
        if (info is null)
            throw new InvalidCredentialsException();

        // Active members for this Google identity across all tenants (invite-only: no auto-create).
        var users = await _db.Users.IgnoreQueryFilters()
            .Where(u => !u.IsDeleted && u.IsActive && (u.GoogleId == info.Subject || u.Email == info.Email))
            .Select(u => new { u.Id, u.TenantId })
            .ToListAsync(ct);
        if (users.Count == 0)
            return [];

        var tenantIds = users.Select(u => u.TenantId).Distinct().ToList();
        var tenants = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => tenantIds.Contains(t.Id) && !t.IsDeleted)
            .Select(t => new { t.Id, t.Name, t.Subdomain })
            .ToListAsync(ct);

        var workspaces = new List<GoogleWorkspaceDto>();
        foreach (var tenant in tenants)
        {
            var user = users.First(u => u.TenantId == tenant.Id);
            var grant = _tokens.GenerateHandoffToken(user.Id, tenant.Id);
            var baseUrl = _settings.TenantUrlFormat.Replace("{subdomain}", tenant.Subdomain);
            var handoffUrl = $"{baseUrl}/auth/handoff?grant={Uri.EscapeDataString(grant)}";
            workspaces.Add(new GoogleWorkspaceDto(tenant.Subdomain, tenant.Name, handoffUrl));
        }

        return workspaces.OrderBy(w => w.TenantName).ToList();
    }
}
