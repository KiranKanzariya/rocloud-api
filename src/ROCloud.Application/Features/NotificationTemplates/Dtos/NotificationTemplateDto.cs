namespace ROCloud.Application.Features.NotificationTemplates.Dtos;

/// <summary>
/// A notification template (guide §24). <see cref="IsCustom"/> is true when the row is the tenant's
/// own override, false when it is the shared system default the tenant is inheriting.
/// </summary>
public sealed record NotificationTemplateDto(
    Guid Id,
    string TemplateCode,
    string LanguageCode,
    string Channel,
    string? Subject,
    string Body,
    DateTime? UpdatedAt,
    bool IsCustom);
