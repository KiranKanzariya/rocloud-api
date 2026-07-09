using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.ServiceRequests.Dtos;

namespace ROCloud.Application.Features.ServiceRequests.Queries.GetServiceRequestById;

public sealed record GetServiceRequestByIdQuery(Guid Id) : IRequest<ServiceRequestDto>;

public class GetServiceRequestByIdQueryHandler : IRequestHandler<GetServiceRequestByIdQuery, ServiceRequestDto>
{
    private readonly IAppDbContext _db;

    public GetServiceRequestByIdQueryHandler(IAppDbContext db) => _db = db;

    public async Task<ServiceRequestDto> Handle(GetServiceRequestByIdQuery request, CancellationToken ct)
    {
        var s = await _db.ServiceRequests
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException("ServiceRequest", request.Id);

        var techName = s.AssignedTechId is { } techId
            ? await _db.Users.Where(u => u.Id == techId).Select(u => u.Name).FirstOrDefaultAsync(ct)
            : null;

        return new ServiceRequestDto(
            s.Id, s.TicketNumber, s.CustomerId,
            s.Customer?.Name ?? string.Empty, s.Customer?.Mobile,
            s.Title, s.Description, s.ServiceType.ToString(), s.Status.ToString(), s.Priority.ToString(),
            s.AssignedTechId, techName, s.ScheduledDate, s.ResolvedAt, s.ResolutionNotes, s.CreatedAt);
    }
}
