using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIGER.API.Authorization.Policies;
using SIGER.API.Extensions;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Products;
using SIGER.Application.Interfaces.Services;

namespace SIGER.API.Controllers;

[ApiController]
[Route("api/v1/products")]
[Authorize(Policy = ApiPolicies.StaffOnly)]
public sealed class ProductsController(IProductService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<ProductDto>), 200)]
    public async Task<IActionResult> GetPaged([Range(1, int.MaxValue)] int pageNumber = 1, [Range(1, 200)] int pageSize = 20,
        long? categoryId = null, CancellationToken cancellationToken = default) =>
        (await service.GetPagedAsync(pageNumber, pageSize, categoryId, cancellationToken)).ToHttp(this);

    [HttpGet("available")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("Public")]
    [ProducesResponseType(typeof(IReadOnlyCollection<ProductDto>), 200)]
    public async Task<IActionResult> GetAvailable(long? categoryId = null, CancellationToken cancellationToken = default) =>
        (await service.GetAvailableAsync(categoryId, cancellationToken)).ToHttp(this);

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ProductDto), 200)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken) =>
        (await service.GetByIdAsync(id, cancellationToken)).ToHttp(this);

    [HttpPost]
    [Authorize(Policy = ApiPolicies.AdministratorOnly)]
    [ProducesResponseType(typeof(ProductDto), 201)]
    public async Task<IActionResult> Create(CreateProductRequestDto request, CancellationToken cancellationToken) =>
        (await service.CreateAsync(request, cancellationToken)).ToHttp(this, nameof(GetById), value => new { id = value.Id });

    [HttpPut("{id:long}")]
    [Authorize(Policy = ApiPolicies.AdministratorOnly)]
    [ProducesResponseType(typeof(ProductDto), 200)]
    public async Task<IActionResult> Update(long id, UpdateProductRequestDto request, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(id, request, cancellationToken)).ToHttp(this);

    [HttpPatch("{id:long}/availability")]
    [Authorize(Policy = ApiPolicies.AdministratorOnly)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ChangeAvailability(long id, UpdateProductAvailabilityRequestDto request, CancellationToken cancellationToken) =>
        (await service.ChangeAvailabilityAsync(id, request, cancellationToken)).ToHttp(this);
}
