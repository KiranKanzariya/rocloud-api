using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.API.Controllers;

/// <summary>
/// Serves time-limited, signed download URLs produced by LocalFileStorage (guide §4b.6).
/// The URL itself is signed and expiry-checked, so the endpoint is anonymous.
/// </summary>
[ApiController]
[Route("api/files")]
[AllowAnonymous]
public class FilesController : ControllerBase
{
    private readonly IFileStorage _storage;
    private readonly IDataProtector _protector;

    public FilesController(IFileStorage storage, IDataProtectionProvider dpp)
    {
        _storage = storage;
        _protector = dpp.CreateProtector("ROCloud.FileDownloads");
    }

    [HttpGet("{token}")]
    public async Task<IActionResult> Download(string token, CancellationToken ct)
    {
        string payload;
        try { payload = _protector.Unprotect(token); }
        catch { return NotFound(); }

        var parts = payload.Split('|');
        if (parts.Length != 2) return NotFound();

        var path = parts[0];
        if (!long.TryParse(parts[1], out var expiryUnix)) return NotFound();

        var expiry = DateTimeOffset.FromUnixTimeSeconds(expiryUnix);
        if (DateTimeOffset.UtcNow > expiry) return NotFound();

        if (!await _storage.ExistsAsync(path, ct)) return NotFound();

        var stream = await _storage.DownloadAsync(path, ct);
        return File(stream, "application/octet-stream");
    }
}
