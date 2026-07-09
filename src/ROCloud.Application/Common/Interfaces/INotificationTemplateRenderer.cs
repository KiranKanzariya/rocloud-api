namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// Resolves an outbound message from the <c>notification_templates</c> table and substitutes its
/// <c>{{Token}}</c> placeholders. Resolution for a (templateCode, channel): the tenant's own row
/// wins over the system default (<c>tenant_id IS NULL</c>), and the requested language wins over the
/// <c>en</c> fallback. Returns <see langword="null"/> when no template exists, so callers keep their
/// built-in default text and nothing breaks if a template row is missing.
/// </summary>
public interface INotificationTemplateRenderer
{
    Task<RenderedMessage?> RenderAsync(
        Guid? tenantId, string templateCode, string? languageCode, string channel,
        IReadOnlyDictionary<string, string> tokens, CancellationToken ct = default);
}

/// <summary>A rendered message. <see cref="Subject"/> is null for subject-less channels (SMS/WhatsApp).</summary>
public sealed record RenderedMessage(string? Subject, string Body);
