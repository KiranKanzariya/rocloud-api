namespace ROCloud.Application.Features.Areas.Dtos;

/// <summary>A delivery area / zone (guide §24). Backed by the areas table.</summary>
public sealed record AreaDto(
    Guid Id,
    string Name,
    string? City,
    string? Pincode,
    bool IsActive,
    int CustomerCount);
