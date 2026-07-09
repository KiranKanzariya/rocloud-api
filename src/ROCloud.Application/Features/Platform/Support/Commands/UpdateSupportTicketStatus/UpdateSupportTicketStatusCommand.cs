using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Sanitisation;

namespace ROCloud.Application.Features.Platform.Support.Commands.UpdateSupportTicketStatus;

/// <summary>Updates a support ticket's status, with a resolution note on Resolved/Closed (guide §26).</summary>
public sealed record UpdateSupportTicketStatusCommand(
    Guid Id,
    string Status,
    [property: SanitizeHtml] string? ResolutionNote) : IRequest;

public class UpdateSupportTicketStatusCommandValidator : AbstractValidator<UpdateSupportTicketStatusCommand>
{
    private static readonly string[] Statuses = ["Open", "InProgress", "Resolved", "Closed"];

    public UpdateSupportTicketStatusCommandValidator()
    {
        RuleFor(c => c.Status).Must(v => Statuses.Contains(v)).WithMessage("Invalid status.");
        RuleFor(c => c.ResolutionNote).MaximumLength(2000);
    }
}

public class UpdateSupportTicketStatusCommandHandler : IRequestHandler<UpdateSupportTicketStatusCommand>
{
    private readonly IAppDbContext _db;

    public UpdateSupportTicketStatusCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(UpdateSupportTicketStatusCommand request, CancellationToken ct)
    {
        var ticket = await _db.SupportTickets.FirstOrDefaultAsync(t => t.Id == request.Id, ct)
                     ?? throw new NotFoundException("SupportTicket", request.Id);

        ticket.Status = request.Status;
        if (request.Status is "Resolved" or "Closed" && !string.IsNullOrWhiteSpace(request.ResolutionNote))
            ticket.ResolutionNote = request.ResolutionNote;

        await _db.SaveChangesAsync(ct);
    }
}
