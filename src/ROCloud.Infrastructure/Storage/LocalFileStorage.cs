using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.Storage;

/// <summary>
/// v1 local-disk implementation of <see cref="IFileStorage"/> (guide §4b.6).
/// Registered as scoped. Includes path-traversal protection. Swap to an
/// S3/Supabase implementation later without touching any caller.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly string _root;             // e.g. App_Data/files or /var/lib/rocloud/files
    private readonly IDataProtector _protector;
    private readonly IHttpContextAccessor _http;

    public LocalFileStorage(
        IConfiguration config,
        IDataProtectionProvider dpp,
        IHttpContextAccessor http)
    {
        _root = config["Storage:LocalPath"]
                ?? throw new InvalidOperationException("Storage:LocalPath not configured");
        _protector = dpp.CreateProtector("ROCloud.FileDownloads");
        _http = http;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> UploadAsync(
        Stream content, string contentType, Guid tenantId, string folder, string fileName,
        CancellationToken ct = default)
    {
        // Path: {root}/{tenantId}/{folder}/{fileName}
        var dir = Path.Combine(_root, tenantId.ToString(), folder);
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, fileName);
        var relPath = Path.Combine(tenantId.ToString(), folder, fileName)
                          .Replace('\\', '/');

        // Prevent path traversal — refuse if the resolved path escapes _root
        var resolvedPath = Path.GetFullPath(fullPath);
        if (!resolvedPath.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Path traversal detected");

        await using var fs = File.Create(fullPath);
        await content.CopyToAsync(fs, ct);
        return relPath;
    }

    public Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
    {
        var fullPath = ResolveSafePath(path);
        return Task.FromResult<Stream>(File.OpenRead(fullPath));
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => Task.FromResult(File.Exists(ResolveSafePath(path)));

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var fullPath = ResolveSafePath(path);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public string GetDownloadUrl(string path, TimeSpan expiry)
    {
        // Encode path + expiry, sign with IDataProtector, return URL
        var payload = $"{path}|{DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds()}";
        var token = _protector.Protect(payload);
        var encoded = Uri.EscapeDataString(token);
        var request = _http.HttpContext?.Request;
        var baseUrl = request is null ? string.Empty : $"{request.Scheme}://{request.Host}";
        return $"{baseUrl}/api/files/{encoded}";
    }

    private string ResolveSafePath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!full.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Path traversal detected");
        return full;
    }
}
