using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Platform.Plans.Commands.DeletePlan;

/// <summary>Deletes a plan. Blocked while any tenant is on it (guide §26).</summary>
public sealed record DeletePlanCommand(Guid Id) : IRequest;

public class DeletePlanCommandHandler : IRequestHandler<DeletePlanCommand>
{
    private readonly IAppDbContext _db;

    public DeletePlanCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(DeletePlanCommand request, CancellationToken ct)
    {
        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == request.Id, ct)
                   ?? throw new NotFoundException("Plan", request.Id);

        var inUse = await _db.Tenants.AnyAsync(t => t.PlanId == plan.Id && !t.IsDeleted, ct);
        if (inUse)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["plan"] = ["This plan has tenants on it and cannot be deleted. Deactivate it instead."]
            });

        _db.Plans.Remove(plan);
        await _db.SaveChangesAsync(ct);
    }
}
