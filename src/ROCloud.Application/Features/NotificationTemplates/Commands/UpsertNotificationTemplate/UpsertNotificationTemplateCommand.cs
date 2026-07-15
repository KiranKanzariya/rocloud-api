using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Sanitisation;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Features.NotificationTemplates.Commands.UpsertNotificationTemplate;

/// <summary>
/// Creates or updates the tenant's template for a (templateCode, languageCode, channel) tuple.
/// Keyed on that tuple — a second call with the same key updates the existing row.
/// </summary>
public sealed record UpsertNotificationTemplateCommand(
    string TemplateCode,
    string LanguageCode,
    string Channel,
    string? Subject,
    [property: SanitizeHtml] string Body) : IRequest<Guid>;

public class UpsertNotificationTemplateCommandValidator : AbstractValidator<UpsertNotificationTemplateCommand>
{
    private static readonly string[] Channels = ["Email", "SMS", "WhatsApp"];

    public UpsertNotificationTemplateCommandValidator()
    {
        RuleFor(c => c.TemplateCode).NotEmpty().MaximumLength(50);

        // A platform→tenant mail (welcome, password_reset, subscription_*) is rendered with a null
        // tenant id, so an override for it would never be read. Refuse it rather than store a row the
        // owner believes is live. These are hidden from the list too; this stops a hand-made request.
        RuleFor(c => c.TemplateCode)
            .Must(code => !PlatformTemplates.IsPlatformOnly(code))
            .WithMessage("This template is sent by ROCloud, not by your business, and cannot be customised.");
        RuleFor(c => c.LanguageCode).NotEmpty().MaximumLength(5);
        RuleFor(c => c.Channel)
            .Must(v => Channels.Contains(v))
            .WithMessage("Channel must be Email, SMS, or WhatsApp.");
        RuleFor(c => c.Subject).MaximumLength(200);
        RuleFor(c => c.Body).NotEmpty().MaximumLength(4000);
    }
}

public class UpsertNotificationTemplateCommandHandler
    : IRequestHandler<UpsertNotificationTemplateCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public UpsertNotificationTemplateCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(UpsertNotificationTemplateCommand request, CancellationToken ct)
    {
        var existing = await _db.NotificationTemplates.FirstOrDefaultAsync(
            t => t.TenantId == _tenant.TenantId
                 && t.TemplateCode == request.TemplateCode
                 && t.LanguageCode == request.LanguageCode
                 && t.Channel == request.Channel, ct);

        if (existing is not null)
        {
            existing.Subject = request.Subject;
            existing.Body = request.Body;
            await _db.SaveChangesAsync(ct);
            return existing.Id;
        }

        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            TemplateCode = request.TemplateCode,
            LanguageCode = request.LanguageCode,
            Channel = request.Channel,
            Subject = request.Subject,
            Body = request.Body
        };
        _db.NotificationTemplates.Add(template);
        await _db.SaveChangesAsync(ct);
        return template.Id;
    }
}
