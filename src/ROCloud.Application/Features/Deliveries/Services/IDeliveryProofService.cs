namespace ROCloud.Application.Features.Deliveries.Services;

/// <summary>
/// Validates and stores a delivery proof photo (guide §10.11). The API layer reads the
/// uploaded file and passes the raw bytes — this service stays free of any AspNetCore
/// dependency and goes through <see cref="Common.Interfaces.IFileStorage"/> only.
/// </summary>
public interface IDeliveryProofService
{
    /// <summary>
    /// Validates (size, extension, MIME, magic bytes), re-encodes to JPEG to strip any
    /// embedded payload, stores under "{tenantId}/delivery-proofs/{guid}.jpg" and returns
    /// the stored relative path.
    /// </summary>
    Task<string> SaveAsync(
        byte[] content,
        string fileName,
        string contentType,
        Guid tenantId,
        CancellationToken ct = default);
}
