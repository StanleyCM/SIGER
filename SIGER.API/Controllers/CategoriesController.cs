using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIGER.API.Authorization.Policies;
using SIGER.API.Extensions;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Categories;
using SIGER.Application.Interfaces.Services;

namespace SIGER.API.Controllers;

[ApiController]
[Route("api/v1/categories")]
[Authorize(Policy = ApiPolicies.AdministratorOnly)]
public sealed class CategoriesController(ICategoryService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<CategoryDto>), 200)]
    public async Task<IActionResult> GetPaged([Range(1, int.MaxValue)] int pageNumber = 1, [Range(1, 200)] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        (await service.GetPagedAsync(pageNumber, pageSize, cancellationToken)).ToHttp(this);

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(CategoryDto), 200)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken) =>
        (await service.GetByIdAsync(id, cancellationToken)).ToHttp(this);

    [HttpPost]
    [ProducesResponseType(typeof(CategoryDto), 201)]
    public async Task<IActionResult> Create(CreateCategoryRequestDto request, CancellationToken cancellationToken) =>
        (await service.CreateAsync(request, cancellationToken)).ToHttp(this, nameof(GetById), value => new { id = value.Id });

    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(CategoryDto), 200)]
    public async Task<IActionResult> Update(long id, UpdateCategoryRequestDto request, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(id, request, cancellationToken)).ToHttp(this);

    [HttpPatch("{id:long}/status")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> SetActive(long id, SIGER.API.Configuration.UpdateActiveRequest request, CancellationToken cancellationToken) =>
        (await service.SetActiveAsync(id, request.IsActive, cancellationToken)).ToHttp(this);
}
