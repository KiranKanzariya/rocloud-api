using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Application.Common.Services;

/// <inheritdoc cref="INotificationTemplateRenderer"/>
public sealed partial class NotificationTemplateRenderer : INotificationTemplateRenderer
{
    private readonly IAppDbContext _db;

    public NotificationTemplateRenderer(IAppDbContext db) => _db = db;

    public async Task<RenderedMessage?> RenderAsync(
        Guid? tenantId, string templateCode, string? languageCode, string channel,
        IReadOnlyDictionary<string, string> tokens, CancellationToken ct = default)
    {
        var lang = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();

        // Candidate rows: this tenant's overrides plus the system defaults (tenant_id NULL), in the
        // requested language or the "en" fallback. The set is tiny, so pick the best match in memory.
        var candidates = await _db.NotificationTemplates.AsNoTracking()
            .Where(t => t.TemplateCode == templateCode && t.Channel == channel
                        && (t.TenantId == tenantId || t.TenantId == null)
                        && (t.LanguageCode == lang || t.LanguageCode == "en"))
            .ToListAsync(ct);

        if (candidates.Count == 0) return null;

        // Prefer a tenant-specific row over the shared default, then the exact language over "en".
        var best = candidates
            .OrderByDescending(t => tenantId != null && t.TenantId == tenantId)
            .ThenByDescending(t => t.LanguageCode == lang)
            .First();

        return new RenderedMessage(Substitute(best.Subject, tokens), Substitute(best.Body, tokens) ?? string.Empty);
    }

    /// <summary>
    /// Replaces every <c>{{Token}}</c> (case-insensitive, tolerant of inner spaces) with its value.
    /// Unknown tokens are left untouched so a typo is visible rather than silently blanked.
    /// </summary>
    private static string? Substitute(string? template, IReadOnlyDictionary<string, string> tokens)
    {
        if (string.IsNullOrEmpty(template)) return template;
        return TokenPattern().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            foreach (var kv in tokens)
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            return match.Value;
        });
    }

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex TokenPattern();
}
