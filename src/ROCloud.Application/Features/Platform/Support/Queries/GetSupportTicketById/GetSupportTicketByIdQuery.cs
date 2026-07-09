using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.Support.Dtos;

namespace ROCloud.Application.Features.Platform.Support.Queries.GetSupportTicketById;

/// <summary>Full detail for one support ticket.</summary>
public sealed record GetSupportTicketByIdQuery(Guid Id) : IRequest<SupportTicketDto>;

public class GetSupportTicketByIdQueryHandler : IRequestHandler<GetSupportTicketByIdQuery, SupportTicketDto>
{
    private readonly IAppDbContext _db;

    public GetSupportTicketByIdQueryHandler(IAppDbContext db) => _db = db;

    public async Task<SupportTicketDto> Handle(GetSupportTicketByIdQuery request, CancellationToken ct)
    {
        var t = await _db.SupportTickets
            .Include(x => x.Tenant)
            .Include(x => x.AssignedPlatformUser)
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException("SupportTicket", request.Id);

        return new SupportTicketDto(
            t.Id, t.TenantId, t.Tenant?.Name ?? "", t.Subject, t.Description, t.Status, t.Priority,
            t.AssignedPlatformUserId, t.AssignedPlatformUser?.Name, t.ResolutionNote, t.CreatedAt, t.UpdatedAt);
    }
}
