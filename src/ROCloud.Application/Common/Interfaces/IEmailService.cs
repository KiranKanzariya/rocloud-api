namespace ROCloud.Application.Common.Interfaces;

/// <summary>A file attached to an outgoing email — e.g. the invoice PDF (guide §14).</summary>
public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);

/// <summary>
/// Transactional email. The 3-arg overload sends a plain HTML mail; the overload taking
/// <see cref="EmailAttachment"/>s attaches files (e.g. the invoice PDF is now attached rather
/// than linked). Providers that don't support attachments fall back to the attachment-less send
/// via the default implementation below, so existing implementations (and test doubles) are unaffected.
/// </summary>
public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);

    /// <summary>
    /// Send with file attachments. Defaults to the attachment-less send for providers that don't
    /// support attachments — only the active provider (Resend/SendGrid) and the branding decorator
    /// override this to carry the files through.
    /// </summary>
    Task SendAsync(
        string to, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment> attachments, CancellationToken ct = default)
        => SendAsync(to, subject, htmlBody, ct);
}
