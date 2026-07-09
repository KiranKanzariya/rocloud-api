namespace ROCloud.Application.Features.Plans.Dtos;

/// <summary>A subscription plan ROCloud offers to tenants (guide §25).</summary>
public sealed record PlanDto(
    Guid Id,
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
    bool ApiAccessEnabled);
