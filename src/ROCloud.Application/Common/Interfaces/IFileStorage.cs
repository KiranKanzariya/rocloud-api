namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// File storage abstraction (guide §4b.5). Callers use ONLY this interface —
/// never the file system or AWS SDK directly — so the local-disk → S3/Supabase
/// swap is a one-class change.
/// </summary>
public interface IFileStorage
{
    Task<string> UploadAsync(
        Stream content,
        string contentType,
        Guid tenantId,
        string folder,         // e.g. "delivery-proofs", "invoices", "tenant-logos"
        string fileName,       // already validated + random per guide §10.11
        CancellationToken ct = default);

    Task<Stream> DownloadAsync(string path, CancellationToken ct = default);
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Returns a temporary URL the client can use to download directly.
    /// Local: /api/files/{token} (FilesController serves it). S3: a presigned URL.
    /// </summary>
    string GetDownloadUrl(string path, TimeSpan expiry);
}
