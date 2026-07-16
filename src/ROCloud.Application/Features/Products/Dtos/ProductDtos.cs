namespace ROCloud.Application.Features.Products.Dtos;

/// <summary>A product (bottle/jar size) in the tenant catalogue.</summary>
public sealed record ProductDto(
    Guid Id,
    string Name,
    string BottleSize,
    decimal DefaultRate,
    string Unit,
    string? Hsn,
    bool IsActive,
    DateTime CreatedAt);
