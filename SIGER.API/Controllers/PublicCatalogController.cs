using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SIGER.API.Extensions;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Catalog;
using SIGER.Application.Interfaces.Services;

namespace SIGER.API.Controllers;

[ApiController, AllowAnonymous, Route("api/v1/public"), EnableRateLimiting("Public")]
public sealed class PublicCatalogController(IPublicCatalogService service) : ControllerBase
{
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyCollection<PublicCategoryDto>), 200)]
    public async Task<IActionResult> Categories(CancellationToken cancellationToken)
        => (await service.GetCategoriesAsync(cancellationToken)).ToHttp(this);

    [HttpGet("products")]
    [ProducesResponseType(typeof(PaginatedResult<PublicProductDto>), 200)]
    public async Task<IActionResult> Products([Range(1, int.MaxValue)] int pageNumber = 1, [Range(1, 100)] int pageSize = 20,
        [Range(1, long.MaxValue)] long? categoryId = null, CancellationToken cancellationToken = default)
        => (await service.GetProductsAsync(pageNumber, pageSize, categoryId, cancellationToken)).ToHttp(this);

    [HttpGet("products/{id:long}")]
    [ProducesResponseType(typeof(PublicProductDto), 200)]
    public async Task<IActionResult> Product(long id, CancellationToken cancellationToken)
        => (await service.GetProductAsync(id, cancellationToken)).ToHttp(this);

    [HttpGet("promotions")]
    [ProducesResponseType(typeof(IReadOnlyCollection<PublicPromotionDto>), 200)]
    public async Task<IActionResult> Promotions(CancellationToken cancellationToken)
        => (await service.GetPromotionsAsync(cancellationToken)).ToHttp(this);
}
