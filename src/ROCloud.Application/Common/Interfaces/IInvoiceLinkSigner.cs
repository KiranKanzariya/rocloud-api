namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// Signs the time-limited invoice download links sent to customers. A customer has no login, so the
/// signed token itself is the authorisation: it carries the tenant and the invoice, and the endpoint
/// re-renders the PDF from the invoice row on each request (no PDF is kept on disk).
/// </summary>
public interface IInvoiceLinkSigner
{
    /// <summary>An absolute, signed, expiring URL to the invoice PDF.</summary>
    string CreateDownloadUrl(Guid tenantId, Guid invoiceId, TimeSpan expiry);

    /// <summary>The tenant + invoice the token authorises, or null when it is malformed or expired.</summary>
    (Guid TenantId, Guid InvoiceId)? Validate(string token);
}
