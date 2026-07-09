using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.ServiceRequests.Commands.UpdateServiceStatus;

/// <summary>
/// Technician transitions a request to InProgress / Resolved / Cancelled. On Resolved it
/// stamps ResolvedAt and stores the resolution notes.
/// </summary>
public sealed record UpdateServiceStatusCommand(
    Guid Id,
    string Status,
    string? ResolutionNotes) : IRequest;

public class UpdateServiceStatusCommandValidator : AbstractValidator<UpdateServiceStatusCommand>
{
    public UpdateServiceStatusCommandValidator()
    {
        RuleFor(c => c.Status)
            .Must(v => Enum.GetNames<ServiceRequestStatus>().Contains(v))
            .WithMessage("Invalid status.");
        RuleFor(c => c.ResolutionNotes).MaximumLength(2000);
    }
}

public class UpdateServiceStatusCommandHandler : IRequestHandler<UpdateServiceStatusCommand>
{
    private readonly IAppDbContext _db;

    public UpdateServiceStatusCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(UpdateServiceStatusCommand request, CancellationToken ct)
    {
        var serviceRequest = await _db.ServiceRequests.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
                             ?? throw new NotFoundException("ServiceRequest", request.Id);

        var status = Enum.Parse<ServiceRequestStatus>(request.Status);
        serviceRequest.Status = status;

        if (status == ServiceRequestStatus.Resolved)
        {
            serviceRequest.ResolvedAt = DateTime.UtcNow;
            if (request.ResolutionNotes is not null)
                serviceRequest.ResolutionNotes = request.ResolutionNotes;
        }

        await _db.SaveChangesAsync(ct);
    }
}
