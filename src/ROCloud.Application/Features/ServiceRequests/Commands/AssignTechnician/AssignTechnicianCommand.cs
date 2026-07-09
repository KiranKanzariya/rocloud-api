using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.ServiceRequests.Commands.AssignTechnician;

/// <summary>Assigns a service request to a user — who must hold the Technician role.</summary>
public sealed record AssignTechnicianCommand(Guid Id, Guid TechnicianId) : IRequest;

public class AssignTechnicianCommandHandler : IRequestHandler<AssignTechnicianCommand>
{
    public const string TechnicianRole = "Technician";

    private readonly IAppDbContext _db;

    public AssignTechnicianCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(AssignTechnicianCommand request, CancellationToken ct)
    {
        var serviceRequest = await _db.ServiceRequests.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
                             ?? throw new NotFoundException("ServiceRequest", request.Id);

        var isTechnician = await _db.Users.AnyAsync(
            u => u.Id == request.TechnicianId
                 && u.IsActive
                 && u.Role != null
                 && u.Role.Name == TechnicianRole, ct);

        if (!isTechnician)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["technicianId"] = ["The selected user is not an active technician."]
            });

        serviceRequest.AssignedTechId = request.TechnicianId;
        await _db.SaveChangesAsync(ct);
    }
}
