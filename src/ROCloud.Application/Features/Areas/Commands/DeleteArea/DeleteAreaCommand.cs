using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Areas.Commands.DeleteArea;

/// <summary>Soft-deletes a delivery area. Blocked while customers are assigned to it.</summary>
public sealed record DeleteAreaCommand(Guid Id) : IRequest;

public class DeleteAreaCommandHandler : IRequestHandler<DeleteAreaCommand>
{
    private readonly IAppDbContext _db;

    public DeleteAreaCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(DeleteAreaCommand request, CancellationToken ct)
    {
        var area = await _db.Areas.FirstOrDefaultAsync(a => a.Id == request.Id, ct)
                   ?? throw new NotFoundException("Area", request.Id);

        var hasCustomers = await _db.Customers.AnyAsync(c => c.AreaId == area.Id, ct);
        if (hasCustomers)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["area"] = ["This area has customers assigned and cannot be deleted. Reassign them first."]
            });

        area.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }
}
