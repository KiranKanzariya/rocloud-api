using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Sanitisation;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Features.Platform.NotificationTemplates.Commands.UpsertDefaultNotificationTemplate;

/// <summary>
/// Creates or updates a SYSTEM-DEFAULT template (tenant_id IS NULL) for a
/// (templateCode, languageCode, channel) tuple. A change here alters the message for every tenant
/// that has not overridden it, so this is SuperAdmin-only (enforced at the controller). Mirrors the
/// tenant upsert but scoped to the NULL-tenant rows.
/// </summary>
// Body is NOT marked [SanitizeHtml] (which would run the strict default sanitiser). Admin email
// templates need the wider email-HTML sanitiser applied in the handler instead — see below.
public sealed record UpsertDefaultNotificationTemplateCommand(
    string TemplateCode,
    string LanguageCode,
    string Channel,
    string? Subject,
    string Body) : IRequest<Guid>;

public class UpsertDefaultNotificationTemplateCommandValidator
    : AbstractValidator<UpsertDefaultNotificationTemplateCommand>
{
    private static readonly string[] Channels = ["Email", "SMS", "WhatsApp"];

    public UpsertDefaultNotificationTemplateCommandValidator()
    {
        RuleFor(c => c.TemplateCode).NotEmpty().MaximumLength(50);
        RuleFor(c => c.LanguageCode).NotEmpty().MaximumLength(5);
        RuleFor(c => c.Channel)
            .Must(v => Channels.Contains(v))
            .WithMessage("Channel must be Email, SMS, or WhatsApp.");
        RuleFor(c => c.Subject).MaximumLength(200);
        RuleFor(c => c.Body).NotEmpty().MaximumLength(20000);   // room for a full HTML email
    }
}

public class UpsertDefaultNotificationTemplateCommandHandler
    : IRequestHandler<UpsertDefaultNotificationTemplateCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IEmailHtmlSanitizer _sanitizer;

    public UpsertDefaultNotificationTemplateCommandHandler(IAppDbContext db, IEmailHtmlSanitizer sanitizer)
    {
        _db = db;
        _sanitizer = sanitizer;
    }

    public async Task<Guid> Handle(UpsertDefaultNotificationTemplateCommand request, CancellationToken ct)
    {
        // Strip scripts/handlers but keep the table + inline-style markup HTML email needs.
        var body = _sanitizer.Sanitize(request.Body);

        // Keyed on (NULL tenant, code, language, channel). EF renders `TenantId == null` as IS NULL,
        // so this finds the existing default row rather than inserting a duplicate.
        var existing = await _db.NotificationTemplates.FirstOrDefaultAsync(
            t => t.TenantId == null
                 && t.TemplateCode == request.TemplateCode
                 && t.LanguageCode == request.LanguageCode
                 && t.Channel == request.Channel, ct);

        if (existing is not null)
        {
            existing.Subject = request.Subject;
            existing.Body = body;
            await _db.SaveChangesAsync(ct);
            return existing.Id;
        }

        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = null,   // system default
            TemplateCode = request.TemplateCode,
            LanguageCode = request.LanguageCode,
            Channel = request.Channel,
            Subject = request.Subject,
            Body = body
        };
        _db.NotificationTemplates.Add(template);
        await _db.SaveChangesAsync(ct);
        return template.Id;
    }
}
