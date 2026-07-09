namespace ROCloud.Application.Features.Platform.Audit.Dtos;

/// <summary>The global activity-log configuration shown/edited in the admin portal.</summary>
public sealed record AuditSettingsDto(
    bool Enabled,
    bool CaptureRequestBody,
    int MaxRequestBodyBytes,
    IReadOnlyList<string> Methods,
    IReadOnlyList<string> SensitivePathPrefixes,
    IReadOnlyList<string> ExcludeModules,
    IReadOnlyList<string> AuditReadsForModules,
    IReadOnlyList<string> AdditionalRedactKeys,
    int RetentionMonths,
    DateTime? UpdatedAt);
