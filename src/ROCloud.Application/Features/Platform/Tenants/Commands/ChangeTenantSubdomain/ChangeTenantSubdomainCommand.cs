using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Auth.Common;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Platform.Tenants.Commands.ChangeTenantSubdomain;

/// <summary>
/// Renames a tenant's subdomain (platform admin action, guide §26) — the only way to fix a
/// wrong-but-valid subdomain, which is otherwise permanent. Normalises the input to a slug, then
/// rejects reserved host labels and any subdomain already in use by another live tenant. Returns
/// the normalised subdomain. NOTE: the tenant's portal URL changes; existing sessions keep working
/// (JWT carries tenant_id, not the subdomain) but users must use the new URL going forward.
/// </summary>
public sealed record ChangeTenantSubdomainCommand(Guid TenantId, string Subdomain) : IRequest<string>;

public class ChangeTenantSubdomainCommandValidator : AbstractValidator<ChangeTenantSubdomainCommand>
{
    public ChangeTenantSubdomainCommandValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.Subdomain).NotEmpty();
    }
}

public class ChangeTenantSubdomainCommandHandler : IRequestHandler<ChangeTenantSubdomainCommand, string>
{
    // Reserved names now live in ReservedSubdomains — this used to keep its own shorter copy, and
    // registration (which is where a tenant actually picks a name) had no list at all.

    private readonly IAppDbContext _db;

    public ChangeTenantSubdomainCommandHandler(IAppDbContext db) => _db = db;

    public async Task<string> Handle(ChangeTenantSubdomainCommand request, CancellationToken ct)
    {
        var slug = SubdomainSlug.From(request.Subdomain);
        if (slug.Length < 3 || slug.Length > 63)
            throw Invalid("Enter a valid subdomain (3–63 letters, numbers or hyphens).");
        if (ReservedSubdomains.IsReserved(slug))
            throw Invalid($"'{slug}' is reserved and can't be used.");

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == request.TenantId && !t.IsDeleted, ct)
            ?? throw new NotFoundException("Tenant", request.TenantId);

        if (string.Equals(tenant.Subdomain, slug, StringComparison.Ordinal))
            return slug;   // no-op

        var taken = await _db.Tenants.IgnoreQueryFilters()
            .AnyAsync(t => t.Id != tenant.Id && t.Subdomain == slug && !t.IsDeleted, ct);
        if (taken)
            throw Invalid($"The subdomain '{slug}' is already taken.");

        tenant.Subdomain = slug;
        await _db.SaveChangesAsync(ct);
        return slug;
    }

    private static ValidationException Invalid(string message)
        => new(new Dictionary<string, string[]> { ["subdomain"] = [message] });
}
