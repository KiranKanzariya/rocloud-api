using MediatR;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.AuditLogs.Queries.GetAuditAvailability;

/// <summary>Whether the activity log is currently enabled — drives the owner portal's menu/route.</summary>
public sealed record AuditAvailabilityDto(bool Enabled);

public sealed record GetAuditAvailabilityQuery : IRequest<AuditAvailabilityDto>;

public class GetAuditAvailabilityQueryHandler : IRequestHandler<GetAuditAvailabilityQuery, AuditAvailabilityDto>
{
    private readonly IAuditSettingsProvider _provider;

    public GetAuditAvailabilityQueryHandler(IAuditSettingsProvider provider) => _provider = provider;

    public async Task<AuditAvailabilityDto> Handle(GetAuditAvailabilityQuery request, CancellationToken ct)
    {
        var settings = await _provider.GetAsync(ct);
        return new AuditAvailabilityDto(settings.Enabled);
    }
}
