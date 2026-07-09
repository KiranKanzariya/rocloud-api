using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Platform.Billing.Commands.RefundTransaction;

/// <summary>Marks a paid billing transaction as Refunded (guide §26). SuperAdmin or Finance.</summary>
public sealed record RefundTransactionCommand(Guid Id) : IRequest;

public class RefundTransactionCommandHandler : IRequestHandler<RefundTransactionCommand>
{
    private readonly IAppDbContext _db;

    public RefundTransactionCommandHandler(IAppDbContext db) => _db = db;

    public async Task Handle(RefundTransactionCommand request, CancellationToken ct)
    {
        var txn = await _db.PlatformBillingTransactions.FirstOrDefaultAsync(t => t.Id == request.Id, ct)
                  ?? throw new NotFoundException("BillingTransaction", request.Id);

        if (txn.Status != "Paid")
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = ["Only paid transactions can be refunded."]
            });

        txn.Status = "Refunded";
        await _db.SaveChangesAsync(ct);
    }
}
