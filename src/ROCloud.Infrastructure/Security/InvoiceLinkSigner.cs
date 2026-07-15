using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.Security;

/// <inheritdoc cref="IInvoiceLinkSigner"/>
public class InvoiceLinkSigner : IInvoiceLinkSigner
{
    private readonly IDataProtector _protector;
    private readonly IHttpContextAccessor _http;

    public InvoiceLinkSigner(IDataProtectionProvider dpp, IHttpContextAccessor http)
    {
        _protector = dpp.CreateProtector("ROCloud.InvoiceLinks");
        _http = http;
    }

    public string CreateDownloadUrl(Guid tenantId, Guid invoiceId, TimeSpan expiry)
    {
        var payload = $"{tenantId}|{invoiceId}|{DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds()}";
        var token = Uri.EscapeDataString(_protector.Protect(payload));
        var request = _http.HttpContext?.Request;
        var baseUrl = request is null ? string.Empty : $"{request.Scheme}://{request.Host}";
        return $"{baseUrl}/api/files/invoice/{token}";
    }

    public (Guid TenantId, Guid InvoiceId)? Validate(string token)
    {
        string payload;
        try { payload = _protector.Unprotect(token); }
        catch { return null; }

        var parts = payload.Split('|');
        if (parts.Length != 3) return null;
        if (!Guid.TryParse(parts[0], out var tenantId)) return null;
        if (!Guid.TryParse(parts[1], out var invoiceId)) return null;
        if (!long.TryParse(parts[2], out var expiryUnix)) return null;
        if (DateTimeOffset.UtcNow > DateTimeOffset.FromUnixTimeSeconds(expiryUnix)) return null;

        return (tenantId, invoiceId);
    }
}
