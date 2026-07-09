using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.ServiceRequests.Dtos;
using ROCloud.Application.Features.ServiceRequests.Queries;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.ServiceRequests.Queries.GetMyServiceJobs;

/// <summary>The current technician's own assigned jobs (AssignedTechId == current user).</summary>
public sealed record GetMyServiceJobsQuery(string? Status) : IRequest<IReadOnlyList<ServiceRequestListItemDto>>;

public class GetMyServiceJobsQueryHandler
    : IRequestHandler<GetMyServiceJobsQuery, IReadOnlyList<ServiceRequestListItemDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetMyServiceJobsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<ServiceRequestListItemDto>> Handle(
        GetMyServiceJobsQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new ForbiddenAccessException();

        var query = _db.ServiceRequests.Where(s => s.AssignedTechId == userId);

        if (request.Status is not null && Enum.GetNames<ServiceRequestStatus>().Contains(request.Status))
        {
            var status = Enum.Parse<ServiceRequestStatus>(request.Status);
            query = query.Where(s => s.Status == status);
        }

        return await query
            .OrderBy(s => s.ScheduledDate ?? DateOnly.MaxValue).ThenByDescending(s => s.CreatedAt)
            .ToListItem(_db)
            .ToListAsync(ct);
    }
}
