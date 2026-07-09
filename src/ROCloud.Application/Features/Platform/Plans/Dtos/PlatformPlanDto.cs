namespace ROCloud.Application.Features.Platform.Plans.Dtos;

/// <summary>A subscription plan as managed by the platform admin (guide §26), incl. usage count.</summary>
public sealed record PlatformPlanDto(
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
    bool ApiAccessEnabled,
    bool IsActive,
    int TenantCount);
