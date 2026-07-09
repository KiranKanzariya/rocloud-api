using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Enums;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Features.Platform.Plans.Commands.UpsertPlan;

/// <summary>
/// Creates a plan (Id null) or updates an existing one (guide §26). On create, the PlanType must
/// not already have a plan. SuperAdmin only.
/// </summary>
public sealed record UpsertPlanCommand(
    Guid? Id,
    string Name,
    string PlanType,
    decimal MonthlyPrice,
    decimal YearlyPrice,
    int MaxCustomers,
    int MaxUsers,
    int MaxDeliveryBoys,
    bool WhatsappEnabled,
    bool CustomRolesEnabled,
    bool MultiBranchEnabled,
    bool ApiAccessEnabled,
    bool IsActive) : IRequest<Guid>;

public class UpsertPlanCommandValidator : AbstractValidator<UpsertPlanCommand>
{
    public UpsertPlanCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(50);
        RuleFor(c => c.PlanType).Must(v => Enum.TryParse<PlanType>(v, out _)).WithMessage("Invalid plan type.");
        RuleFor(c => c.MonthlyPrice).GreaterThanOrEqualTo(0);
        RuleFor(c => c.YearlyPrice).GreaterThanOrEqualTo(0);
        // 0 = unlimited (Plan.Unlimited); any positive number is a hard cap.
        RuleFor(c => c.MaxCustomers).GreaterThanOrEqualTo(0);
        RuleFor(c => c.MaxUsers).GreaterThanOrEqualTo(0);
        RuleFor(c => c.MaxDeliveryBoys).GreaterThanOrEqualTo(0);
    }
}

public class UpsertPlanCommandHandler : IRequestHandler<UpsertPlanCommand, Guid>
{
    private readonly IAppDbContext _db;

    public UpsertPlanCommandHandler(IAppDbContext db) => _db = db;

    public async Task<Guid> Handle(UpsertPlanCommand request, CancellationToken ct)
    {
        var planType = Enum.Parse<PlanType>(request.PlanType);

        var plan = request.Id is { } id
            ? await _db.Plans.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Plan", id)
            : new Plan { Id = Guid.NewGuid() };

        if (request.Id is null)
        {
            var exists = await _db.Plans.AnyAsync(p => p.PlanType == planType, ct);
            if (exists)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["planType"] = [$"A plan for {planType} already exists."]
                });
            _db.Plans.Add(plan);
        }

        plan.Name = request.Name;
        plan.PlanType = planType;
        plan.MonthlyPrice = request.MonthlyPrice;
        plan.YearlyPrice = request.YearlyPrice;
        plan.MaxCustomers = request.MaxCustomers;
        plan.MaxUsers = request.MaxUsers;
        plan.MaxDeliveryBoys = request.MaxDeliveryBoys;
        plan.WhatsappEnabled = request.WhatsappEnabled;
        plan.CustomRolesEnabled = request.CustomRolesEnabled;
        plan.MultiBranchEnabled = request.MultiBranchEnabled;
        plan.ApiAccessEnabled = request.ApiAccessEnabled;
        plan.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        return plan.Id;
    }
}
