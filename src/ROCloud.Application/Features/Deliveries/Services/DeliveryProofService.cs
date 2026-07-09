using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Deliveries.Services;

/// <summary>
/// Default proof-photo handler. Implements the guide §10.11 upload-security pipeline and
/// stores via <see cref="IFileStorage"/> so the S3/Supabase swap stays a one-class change.
/// </summary>
public class DeliveryProofService : IDeliveryProofService
{
    private const string Folder = "delivery-proofs";

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private readonly IFileStorage _storage;
    private readonly long _maxFileSize;

    public DeliveryProofService(IFileStorage storage, IAppSettings settings)
    {
        _storage = storage;
        _maxFileSize = settings.DeliveryProofMaxBytes;
    }

    public async Task<string> SaveAsync(
        byte[] content, string fileName, string contentType, Guid tenantId, CancellationToken ct = default)
    {
        // 1. Size
        if (content.Length == 0)
            throw Invalid("The uploaded file is empty.");
        if (content.Length > _maxFileSize)
            throw Invalid($"File too large (max {_maxFileSize / (1024 * 1024)}MB).");

        // 2. Extension whitelist
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw Invalid("File type not allowed.");

        // 3. MIME whitelist
        if (!AllowedMimeTypes.Contains(contentType))
            throw Invalid("Invalid MIME type.");

        // 4. Magic-byte validation — never trust extension or declared MIME.
        if (!IsValidImageFile(content))
            throw Invalid("File content doesn't match its type.");

        // 5. Re-encode to JPEG to strip any embedded payload (EXIF/scripts/polyglots).
        byte[] reEncoded;
        try
        {
            using var image = Image.Load(content);
            using var output = new MemoryStream();
            await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 85 }, ct);
            reEncoded = output.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Invalid("The image could not be processed.");
        }

        // 6. Random filename — never the original.
        var storedName = $"{Guid.NewGuid():N}.jpg";

        // 7. Store via the abstraction (path: {tenantId}/delivery-proofs/{guid}.jpg).
        await using var stream = new MemoryStream(reEncoded);
        return await _storage.UploadAsync(stream, "image/jpeg", tenantId, Folder, storedName, ct);
    }

    private static ValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]> { ["file"] = [message] });

    private static bool IsValidImageFile(byte[] bytes)
    {
        if (bytes.Length < 12) return false;

        // JPEG: FF D8 FF
        var jpeg = bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        // PNG: 89 50 4E 47
        var png = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        // WebP: "RIFF" .... "WEBP"
        var webp = bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                   && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;

        return jpeg || png || webp;
    }
}
