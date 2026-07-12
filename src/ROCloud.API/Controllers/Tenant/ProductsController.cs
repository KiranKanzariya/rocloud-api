using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROCloud.API.Filters;
using ROCloud.Application.Common.Models;
using ROCloud.Application.Features.Products.Commands.CreateProduct;
using ROCloud.Application.Features.Products.Commands.DeleteProduct;
using ROCloud.Application.Features.Products.Commands.UpdateProduct;
using ROCloud.Application.Features.Products.Dtos;
using ROCloud.Application.Features.Products.Queries.GetProductById;
using ROCloud.Application.Features.Products.Queries.GetProducts;

namespace ROCloud.API.Controllers.Tenant;

// No dedicated Products.* permission exists in the seeded set; products are catalogue
// configuration in the same domain family as inventory, so reads use Inventory.View and
// writes use Inventory.Manage. (Revisit with a dedicated permission in Phase 24 if needed.)
[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductsController(IMediator mediator) => _mediator = mediator;

    // Order lines are made of products, so anyone who may view orders must be able to read the
    // catalogue — otherwise a role like CustomerCare holds Orders.Create but gets an empty product
    // dropdown and cannot place an order at all.
    [HttpGet]
    [RequireAnyPermission("Inventory.View", "Orders.View")]
    public async Task<IActionResult> GetProducts([FromQuery] bool includeInactive, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<ProductDto>>.Ok(
            await _mediator.Send(new GetProductsQuery(includeInactive), ct)));

    [HttpGet("{id:guid}")]
    [RequireAnyPermission("Inventory.View", "Orders.View")]
    public async Task<IActionResult> GetProduct(Guid id, CancellationToken ct)
        => Ok(ApiResponse<ProductDto>.Ok(await _mediator.Send(new GetProductByIdQuery(id), ct)));

    [HttpPost]
    [RequirePermission("Inventory.Manage")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetProduct), new { id }, ApiResponse<object>.Ok(new { id }));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("Inventory.Manage")]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateProductCommand(
            id, body.Name, body.BottleSize, body.DefaultRate, body.Unit, body.IsActive), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("Inventory.Manage")]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteProductCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(new { id }));
    }
}

public sealed record UpdateProductRequest(
    string Name, string BottleSize, decimal DefaultRate, string? Unit, bool IsActive);
