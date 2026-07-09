using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Sanitisation;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Application.Features.Platform.Support.Commands.CreateSupportTicket;

/// <summary>Opens a support ticket for a tenant (guide §26).</summary>
public sealed record CreateSupportTicketCommand(
    Guid TenantId,
    string Subject,
    [property: SanitizeHtml] string? Description,
    string? Priority) : IRequest<Guid>;

public class CreateSupportTicketCommandValidator : AbstractValidator<CreateSupportTicketCommand>
{
    private static readonly string[] Priorities = ["Low", "Medium", "High", "Urgent"];

    public CreateSupportTicketCommandValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.Subject).NotEmpty().Length(2, 200);
        RuleFor(c => c.Description).MaximumLength(4000);
        RuleFor(c => c.Priority)
            .Must(v => v is null || Priorities.Contains(v))
            .WithMessage("Invalid priority.");
    }
}

public class CreateSupportTicketCommandHandler : IRequestHandler<CreateSupportTicketCommand, Guid>
{
    private readonly IAppDbContext _db;

    public CreateSupportTicketCommandHandler(IAppDbContext db) => _db = db;

    public async Task<Guid> Handle(CreateSupportTicketCommand request, CancellationToken ct)
    {
        var tenantExists = await _db.Tenants.AnyAsync(t => t.Id == request.TenantId && !t.IsDeleted, ct);
        if (!tenantExists)
            throw new NotFoundException("Tenant", request.TenantId);

        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Subject = request.Subject,
            Description = request.Description,
            Priority = request.Priority ?? "Medium",
            Status = "Open"
        };
        _db.SupportTickets.Add(ticket);
        await _db.SaveChangesAsync(ct);
        return ticket.Id;
    }
}
