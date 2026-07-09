using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Auth.Common;

namespace ROCloud.Application.Features.Auth.Queries.CheckSubdomain;

/// <summary>
/// Live availability check for the registration subdomain field. Slugifies the input exactly as
/// registration will, then reports whether that subdomain is free — so the user sees it before submit.
/// </summary>
public sealed record CheckSubdomainQuery(string Value) : IRequest<SubdomainAvailabilityDto>;

/// <summary>The slug registration would use, and whether it is currently available.</summary>
public sealed record SubdomainAvailabilityDto(string Subdomain, bool Available);

public class CheckSubdomainQueryHandler : IRequestHandler<CheckSubdomainQuery, SubdomainAvailabilityDto>
{
    private readonly IAppDbContext _db;

    public CheckSubdomainQueryHandler(IAppDbContext db) => _db = db;

    public async Task<SubdomainAvailabilityDto> Handle(CheckSubdomainQuery request, CancellationToken ct)
    {
        var sub = SubdomainSlug.From(request.Value ?? string.Empty);

        // Too short to be a valid subdomain (registration requires 3+ chars) → not available.
        if (sub.Length < 3)
            return new SubdomainAvailabilityDto(sub, false);

        var taken = await _db.Tenants.IgnoreQueryFilters()
            .AnyAsync(t => t.Subdomain == sub && !t.IsDeleted, ct);

        return new SubdomainAvailabilityDto(sub, !taken);
    }
}
