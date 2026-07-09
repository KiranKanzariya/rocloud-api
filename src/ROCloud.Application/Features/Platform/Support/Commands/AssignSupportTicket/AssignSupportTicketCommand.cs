using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Platform.Support.Commands.AssignSupportTicket;

/// <summary>Assigns a support ticket to a platform staff member (guide §26).</summary>
public sealed record AssignSupportTicketCommand(Guid Id, Guid PlatformUserId) : IRequest;

public class AssignSupportTicketCommandHandler : IRequestHandler<AssignSupportTicketCommand>
{
    private readonly IAppDbContext _db;

    public AssignSupportTicketCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(AssignSupportTicketCommand request, CancellationToken ct)
    {
        var ticket = await _db.SupportTickets.FirstOrDefaultAsync(t => t.Id == request.Id, ct)
                     ?? throw new NotFoundException("SupportTicket", request.Id);

        var staff = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == request.PlatformUserId && u.IsActive, ct);
        if (staff is null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["platformUserId"] = ["Unknown or inactive platform user."]
            });

        ticket.AssignedPlatformUserId = staff.Id;
        if (ticket.Status == "Open") ticket.Status = "InProgress";
        await _db.SaveChangesAsync(ct);
    }
}
