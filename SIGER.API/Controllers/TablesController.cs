using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIGER.API.Authorization.Policies;
using SIGER.API.Extensions;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Tables;
using SIGER.Application.Interfaces.Services;

namespace SIGER.API.Controllers;

[ApiController]
[Route("api/v1/tables")]
[Authorize(Policy = ApiPolicies.StaffOnly)]
public sealed class TablesController(ITableService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<TableDto>), 200)]
    public async Task<IActionResult> GetPaged([Range(1, int.MaxValue)] int pageNumber = 1, [Range(1, 200)] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        (await service.GetPagedAsync(pageNumber, pageSize, cancellationToken)).ToHttp(this);

    [HttpGet("available")]
    [ProducesResponseType(typeof(IReadOnlyCollection<TableDto>), 200)]
    public async Task<IActionResult> GetAvailable(CancellationToken cancellationToken = default) =>
        (await service.GetAvailableAsync(cancellationToken)).ToHttp(this);

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(TableDto), 200)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken) =>
        (await service.GetByIdAsync(id, cancellationToken)).ToHttp(this);

    [HttpPost]
    [Authorize(Policy = ApiPolicies.AdministratorOnly)]
    [ProducesResponseType(typeof(TableDto), 201)]
    public async Task<IActionResult> Create(CreateTableRequestDto request, CancellationToken cancellationToken) =>
        (await service.CreateAsync(request, cancellationToken)).ToHttp(this, nameof(GetById), value => new { id = value.Id });

    [HttpPut("{id:long}")]
    [Authorize(Policy = ApiPolicies.AdministratorOnly)]
    [ProducesResponseType(typeof(TableDto), 200)]
    public async Task<IActionResult> Update(long id, UpdateTableRequestDto request, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(id, request, cancellationToken)).ToHttp(this);

    [HttpPatch("{id:long}/status")]
    [Authorize(Policy = ApiPolicies.WaiterOrAdministrator)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ChangeStatus(long id, UpdateTableStatusRequestDto request, CancellationToken cancellationToken) =>
        (await service.ChangeStatusAsync(id, request, cancellationToken)).ToHttp(this);
}
