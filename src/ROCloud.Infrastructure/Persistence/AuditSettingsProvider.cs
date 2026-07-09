using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.Persistence;

/// <summary>
/// Loads the global audit settings and caches them (short TTL) so AuditMiddleware — which runs on
/// every request — never touches the database on the hot path. A singleton that opens a scope only
/// on a cache miss. Falls back to safe defaults (keep auditing) if the row can't be read.
/// </summary>
public sealed class AuditSettingsProvider : IAuditSettingsProvider
{
    private const string CacheKey = "audit:settings";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly ICacheService _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditSettingsProvider> _logger;

    public AuditSettingsProvider(
        ICacheService cache, IServiceScopeFactory scopeFactory, ILogger<AuditSettingsProvider> logger)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<AuditSettingsSnapshot> GetAsync(CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<AuditSettingsSnapshot>(CacheKey, ct);
        if (cached is not null) return cached;

        var snapshot = await LoadAsync(ct);
        await _cache.SetAsync(CacheKey, snapshot, Ttl, ct);
        return snapshot;
    }

    public Task InvalidateAsync(CancellationToken ct = default) => _cache.RemoveAsync(CacheKey, ct);

    private async Task<AuditSettingsSnapshot> LoadAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var row = await db.AuditSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            if (row is null) return AuditSettingsSnapshot.Defaults();

            return new AuditSettingsSnapshot
            {
                Enabled = row.Enabled,
                CaptureRequestBody = row.CaptureRequestBody,
                MaxRequestBodyBytes = row.MaxRequestBodyBytes,
                Methods = row.Methods,
                SensitivePathPrefixes = row.SensitivePathPrefixes,
                ExcludeModules = row.ExcludeModules,
                AuditReadsForModules = row.AuditReadsForModules,
                AdditionalRedactKeys = row.AdditionalRedactKeys,
                RetentionMonths = row.RetentionMonths,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let a settings read failure stop auditing — fall back to the safe defaults.
            _logger.LogError(ex, "Failed to load audit settings; using defaults.");
            return AuditSettingsSnapshot.Defaults();
        }
    }
}
