using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.API.Middleware;

/// <summary>
/// Tamper-evident audit logging (guide §10.14). Records mutations and calls to sensitive endpoints
/// to the append-only audit_logs table, with the request body (sensitive fields redacted) as
/// new_values. WHAT gets logged is driven by the SuperAdmin-managed <see cref="IAuditSettingsProvider"/>
/// (cached), with a hardcoded compliance floor that always audits auth/payments mutations. Writes in a
/// finally so failed/denied requests are recorded too, with the final status code.
/// </summary>
public class AuditMiddleware
{
    private static readonly HashSet<string> BuiltInRedactKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "currentPassword", "newPassword", "confirmPassword",
        "token", "refreshToken", "accessToken", "idToken", "secret", "keySecret"
    };

    // Compliance floor — these mutations are ALWAYS audited regardless of configuration, so a
    // misconfiguration can never blind the security trail.
    private static readonly string[] FloorPrefixes = ["/api/auth", "/api/payments"];

    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var settings = await context.RequestServices
            .GetRequiredService<IAuditSettingsProvider>().GetAsync(context.RequestAborted);

        if (!ShouldAudit(context.Request, settings))
        {
            await _next(context);
            return;
        }

        string? body = null;
        if (settings.CaptureRequestBody)
        {
            try { body = await CaptureBodyAsync(context.Request, settings.MaxRequestBodyBytes); }
            catch { /* never block the request on audit body capture */ }
        }

        try
        {
            await _next(context);
        }
        finally
        {
            // Swallow any audit error: this middleware sits outside ExceptionMiddleware, so it must
            // never surface as a request failure.
            try { await WriteAuditAsync(context, body, settings); }
            catch { /* auditing must never break the request it records */ }
        }
    }

    private static async Task WriteAuditAsync(HttpContext context, string? body, AuditSettingsSnapshot settings)
    {
        // Resolve per-request services after the pipeline ran (tenant/user are populated).
        var writer = context.RequestServices.GetRequiredService<IAuditLogWriter>();
        var tenant = context.RequestServices.GetRequiredService<ITenantContext>();
        var currentUser = context.RequestServices.GetRequiredService<ICurrentUserService>();

        var path = context.Request.Path.Value ?? string.Empty;
        var entry = new AuditEntry(
            TenantId: tenant.TenantId == Guid.Empty ? null : tenant.TenantId,
            UserId: currentUser.UserId,
            Module: ExtractModule(path),
            Action: context.Request.Method,
            EntityName: null,
            EntityId: ExtractTrailingGuid(path),
            NewValues: Redact(body, settings.AdditionalRedactKeys),
            IpAddress: context.Connection.RemoteIpAddress?.ToString(),
            UserAgent: context.Request.Headers.UserAgent.ToString(),
            StatusCode: context.Response.StatusCode);

        await writer.WriteAsync(entry, CancellationToken.None);
    }

    private static bool ShouldAudit(HttpRequest request, AuditSettingsSnapshot s)
    {
        if (!request.Path.StartsWithSegments("/api")) return false;

        // Master switch: when disabled, store nothing at all (no compliance floor).
        if (!s.Enabled) return false;

        var method = request.Method;
        var isMutationVerb = HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
                             || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

        // While enabled, always audit auth/payments mutations even if methods/modules would exclude them.
        if (isMutationVerb && FloorPrefixes.Any(p => request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            return true;

        var module = ExtractModule(request.Path.Value ?? string.Empty);
        if (s.ExcludeModules.Any(m => string.Equals(m, module, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Configured mutation methods.
        if (s.Methods.Any(m => string.Equals(m, method, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Configured sensitive prefixes — audited for any method.
        if (s.SensitivePathPrefixes.Any(p => request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Opt-in read auditing for chosen modules.
        if (HttpMethods.IsGet(method) &&
            s.AuditReadsForModules.Any(m => string.Equals(m, module, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static async Task<string?> CaptureBodyAsync(HttpRequest request, int maxBodyBytes)
    {
        if (request.ContentLength is null or 0 || request.ContentLength > maxBodyBytes) return null;

        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;   // rewind so model binding still works
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    /// <summary>/api/customers/{id} → "customers".</summary>
    private static string ExtractModule(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? segments[1] : "unknown";
    }

    private static Guid? ExtractTrailingGuid(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = segments.Length - 1; i >= 0; i--)
            if (Guid.TryParse(segments[i], out var id))
                return id;
        return null;
    }

    /// <summary>Removes sensitive fields from a JSON body so credentials never land in the audit log.</summary>
    private static string? Redact(string? body, string[] additionalKeys)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        var keys = additionalKeys.Length == 0
            ? BuiltInRedactKeys
            : new HashSet<string>(BuiltInRedactKeys.Concat(additionalKeys), StringComparer.OrdinalIgnoreCase);

        try
        {
            var node = JsonNode.Parse(body);
            RedactNode(node, keys);
            return node?.ToJsonString();
        }
        catch (JsonException)
        {
            return null;   // not JSON — don't store a potentially sensitive raw body
        }
    }

    private static void RedactNode(JsonNode? node, HashSet<string> keys)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    if (keys.Contains(key))
                        obj[key] = "***";
                    else
                        RedactNode(obj[key], keys);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    RedactNode(item, keys);
                break;
        }
    }
}
