using ROCloud.Application.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.ServiceRequests.Dtos;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.ServiceRequests.Commands.ScheduleAmcVisits;

/// <summary>
/// Bulk-creates routine AMC visit tickets from active AMC subscriptions that are due. A
/// subscription is due when its NextDueDate is on or before AsOfDate + LeadDays (default
/// today + 7) and it is not past its EndDate. Each scheduled visit advances the
/// subscription's NextDueDate by its interval so it won't re-fire. Customers that already
/// have an open RoutineAMC ticket are skipped.
/// </summary>
public sealed record ScheduleAmcVisitsCommand(DateOnly? AsOfDate, int? LeadDays)
    : IRequest<AmcScheduleResultDto>;

public class ScheduleAmcVisitsCommandValidator : AbstractValidator<ScheduleAmcVisitsCommand>
{
    public ScheduleAmcVisitsCommandValidator()
    {
        RuleFor(c => c.LeadDays)
            .InclusiveBetween(0, 90).When(c => c.LeadDays.HasValue)
            .WithMessage("LeadDays must be between 0 and 90.");
    }
}

public class ScheduleAmcVisitsCommandHandler : IRequestHandler<ScheduleAmcVisitsCommand, AmcScheduleResultDto>
{
    private static readonly ServiceRequestStatus[] OpenStatuses =
        [ServiceRequestStatus.Open, ServiceRequestStatus.InProgress];

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public ScheduleAmcVisitsCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<AmcScheduleResultDto> Handle(ScheduleAmcVisitsCommand request, CancellationToken ct)
    {
        var asOf = request.AsOfDate ?? AppTimeZone.Today(DateTime.UtcNow);
        var cutoff = asOf.AddDays(request.LeadDays ?? 7);

        var dueSubscriptions = await _db.AmcSubscriptions
            .Where(s => s.IsActive
                        && s.NextDueDate <= cutoff
                        && (s.EndDate == null || s.EndDate >= s.NextDueDate))
            .ToListAsync(ct);

        // Customers already carrying an open RoutineAMC ticket — don't duplicate.
        var alreadyScheduled = (await _db.ServiceRequests
                .Where(s => s.ServiceType == ServiceType.RoutineAMC && OpenStatuses.Contains(s.Status))
                .Select(s => s.CustomerId)
                .ToListAsync(ct))
            .ToHashSet();

        // One sequence read; tickets numbered sequentially in this batch.
        var seq = await _db.ServiceRequests.IgnoreQueryFilters()
            .CountAsync(s => s.TenantId == _tenant.TenantId, ct);

        var created = 0;
        var skipped = 0;

        foreach (var sub in dueSubscriptions)
        {
            if (alreadyScheduled.Contains(sub.CustomerId)) { skipped++; continue; }

            seq++;
            _db.ServiceRequests.Add(new ServiceRequest
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId,
                CustomerId = sub.CustomerId,
                TicketNumber = $"SR-{seq:D4}",
                Title = string.IsNullOrWhiteSpace(sub.PlanName)
                    ? $"Routine AMC visit ({sub.IntervalMonths}-month)"
                    : $"Routine AMC visit — {sub.PlanName}",
                ServiceType = ServiceType.RoutineAMC,
                Priority = ServicePriority.Medium,
                Status = ServiceRequestStatus.Open,
                ScheduledDate = sub.NextDueDate
            });

            // Advance the cadence so this subscription won't be picked up again until next cycle.
            sub.NextDueDate = sub.NextDueDate.AddMonths(sub.IntervalMonths);
            alreadyScheduled.Add(sub.CustomerId);
            created++;
        }

        if (created > 0)
            await _db.SaveChangesAsync(ct);

        return new AmcScheduleResultDto(created, dueSubscriptions.Count, skipped);
    }
}
