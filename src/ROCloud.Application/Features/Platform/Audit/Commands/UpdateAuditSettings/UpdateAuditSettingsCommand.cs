using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.Platform.Audit.Dtos;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Application.Features.Platform.Audit.Commands.UpdateAuditSettings;

/// <summary>Updates the single global audit-settings row (SuperAdmin), then invalidates the cache.</summary>
public sealed record UpdateAuditSettingsCommand(
    bool Enabled,
    bool CaptureRequestBody,
    int MaxRequestBodyBytes,
    IReadOnlyList<string> Methods,
    IReadOnlyList<string> SensitivePathPrefixes,
    IReadOnlyList<string> ExcludeModules,
    IReadOnlyList<string> AuditReadsForModules,
    IReadOnlyList<string> AdditionalRedactKeys,
    int RetentionMonths) : IRequest<AuditSettingsDto>;

public class UpdateAuditSettingsCommandValidator : AbstractValidator<UpdateAuditSettingsCommand>
{
    private static readonly string[] KnownMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    public UpdateAuditSettingsCommandValidator()
    {
        RuleFor(c => c.MaxRequestBodyBytes).InclusiveBetween(0, 5 * 1024 * 1024);
        RuleFor(c => c.RetentionMonths).InclusiveBetween(0, 120);
        RuleFor(c => c.Methods).NotNull()
            .Must(m => m.All(v => KnownMethods.Contains(v.ToUpperInvariant())))
            .WithMessage("Methods must be HTTP verbs (GET/POST/PUT/PATCH/DELETE).");
        RuleFor(c => c.SensitivePathPrefixes).NotNull();
        RuleFor(c => c.ExcludeModules).NotNull();
        RuleFor(c => c.AuditReadsForModules).NotNull();
        RuleFor(c => c.AdditionalRedactKeys).NotNull();
    }
}

public class UpdateAuditSettingsCommandHandler : IRequestHandler<UpdateAuditSettingsCommand, AuditSettingsDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditSettingsProvider _provider;

    public UpdateAuditSettingsCommandHandler(
        IAppDbContext db, ICurrentUserService currentUser, IAuditSettingsProvider provider)
    {
        _db = db;
        _currentUser = currentUser;
        _provider = provider;
    }

    public async Task<AuditSettingsDto> Handle(UpdateAuditSettingsCommand request, CancellationToken ct)
    {
        var s = await _db.AuditSettings.FirstOrDefaultAsync(ct);
        if (s is null)
        {
            s = new AuditSettings { Id = Guid.NewGuid() };
            _db.AuditSettings.Add(s);
        }

        s.Enabled = request.Enabled;
        s.CaptureRequestBody = request.CaptureRequestBody;
        s.MaxRequestBodyBytes = request.MaxRequestBodyBytes;
        s.Methods = Normalize(request.Methods, upper: true);
        s.SensitivePathPrefixes = Normalize(request.SensitivePathPrefixes);
        s.ExcludeModules = Normalize(request.ExcludeModules);
        s.AuditReadsForModules = Normalize(request.AuditReadsForModules);
        s.AdditionalRedactKeys = Normalize(request.AdditionalRedactKeys);
        s.RetentionMonths = request.RetentionMonths;
        s.UpdatedBy = _currentUser.UserId;

        await _db.SaveChangesAsync(ct);
        await _provider.InvalidateAsync(ct); // next request sees the change immediately

        return new AuditSettingsDto(
            s.Enabled, s.CaptureRequestBody, s.MaxRequestBodyBytes,
            s.Methods, s.SensitivePathPrefixes, s.ExcludeModules, s.AuditReadsForModules,
            s.AdditionalRedactKeys, s.RetentionMonths, s.UpdatedAt);
    }

    private static string[] Normalize(IReadOnlyList<string> values, bool upper = false) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v))
              .Select(v => upper ? v.Trim().ToUpperInvariant() : v.Trim())
              .Distinct()
              .ToArray();
}
