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
    /// <summary>
    /// Sends the mail. Returns true only when the provider accepted it — false when the provider is
    /// unconfigured, the Notifications:EmailEnabled master switch is off, or the provider rejected it.
    ///
    /// A provider failure is reported, not thrown: a caller sending to many recipients in a loop must
    /// not have the whole run aborted by one bad address. But it must not be told "sent" either —
    /// callers that record a send (e.g. the reminder throttle's reminder_log) have to gate that write
    /// on this result, or a silent failure is remembered as a success and never retried.
    /// </summary>
    Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);

    /// <summary>
    /// Send with file attachments. Defaults to the attachment-less send for providers that don't
    /// support attachments — only the active provider (Resend/SendGrid) and the branding decorator
    /// override this to carry the files through.
    /// </summary>
    Task<bool> SendAsync(
        string to, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment> attachments, CancellationToken ct = default)
        => SendAsync(to, subject, htmlBody, ct);
}
