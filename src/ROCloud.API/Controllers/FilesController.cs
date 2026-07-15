using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Invoices.Queries.GetInvoicePdf;

namespace ROCloud.API.Controllers;

/// <summary>
/// Serves time-limited, signed download URLs (guide §4b.6): stored files (delivery-proof photos) and
/// the invoice links emailed to customers. Both URLs are signed and expiry-checked, so the endpoints
/// are anonymous — a customer has no login.
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

    /// <summary>
    /// The customer's invoice link from their invoice email. No PDF is stored, so the invoice is
    /// re-rendered from its row on each request. The signed token carries the tenant and is what
    /// authorises the download; scoping the request to that tenant lets the usual query filter
    /// confine the lookup to it.
    /// </summary>
    [HttpGet("invoice/{token}")]
    public async Task<IActionResult> DownloadInvoice(
        string token,
        [FromServices] IInvoiceLinkSigner signer,
        [FromServices] ITenantContext tenant,
        [FromServices] IMediator mediator,
        CancellationToken ct)
    {
        if (signer.Validate(token) is not { } link) return NotFound();

        tenant.TenantId = link.TenantId;   // TenantMiddleware skips /api/files — the token is the scope

        try
        {
            var pdf = await mediator.Send(new GetInvoicePdfQuery(link.InvoiceId), ct);
            return File(pdf.Content, "application/pdf", pdf.FileName);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
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
