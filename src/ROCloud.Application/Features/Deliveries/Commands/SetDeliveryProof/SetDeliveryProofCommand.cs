using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Features.Deliveries.Commands.SetDeliveryProof;

/// <summary>
/// Stores the proof-photo path (already validated + uploaded by DeliveryProofService)
/// on a delivery. Returns the stored path.
/// </summary>
public sealed record SetDeliveryProofCommand(Guid Id, string ProofImageUrl) : IRequest<string>;

public class SetDeliveryProofCommandHandler : IRequestHandler<SetDeliveryProofCommand, string>
{
    private readonly IAppDbContext _db;

    public SetDeliveryProofCommandHandler(IAppDbContext db) => _db = db;

    public async Task<string> Handle(SetDeliveryProofCommand request, CancellationToken ct)
    {
        var delivery = await _db.Deliveries.FirstOrDefaultAsync(d => d.Id == request.Id, ct)
                       ?? throw new NotFoundException("Delivery", request.Id);

        delivery.ProofImageUrl = request.ProofImageUrl;
        await _db.SaveChangesAsync(ct);
        return request.ProofImageUrl;
    }
}
