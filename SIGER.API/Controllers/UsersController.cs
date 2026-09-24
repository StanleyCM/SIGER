using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIGER.API.Authorization.Policies;
using SIGER.API.Extensions;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Users;
using SIGER.Application.Interfaces.Services;

namespace SIGER.API.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = ApiPolicies.AdministratorOnly)]
public sealed class UsersController(IUserService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<UserDto>), 200)]
    public async Task<IActionResult> GetPaged([Range(1, int.MaxValue)] int pageNumber = 1, [Range(1, 200)] int pageSize = 20,
        string? search = null, bool? isActive = null, CancellationToken cancellationToken = default) =>
        (await service.GetPagedAsync(pageNumber, pageSize, search, isActive, cancellationToken)).ToHttp(this);

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(UserDto), 200)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken) =>
        (await service.GetByIdAsync(id, cancellationToken)).ToHttp(this);

    [HttpGet("roles")]
    [ProducesResponseType(typeof(IReadOnlyCollection<SIGER.Application.DTOs.Roles.RoleDto>), 200)]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken) =>
        (await service.GetAvailableRolesAsync(cancellationToken)).ToHttp(this);

    [HttpPost]
    [ProducesResponseType(typeof(UserDto), 201)]
    public async Task<IActionResult> Create(CreateUserRequestDto request, CancellationToken cancellationToken) =>
        (await service.CreateAsync(request, cancellationToken)).ToHttp(this, nameof(GetById), value => new { id = value.Id });

    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(UserDto), 200)]
    public async Task<IActionResult> Update(long id, UpdateUserRequestDto request, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(id, request, cancellationToken)).ToHttp(this);

    [HttpPatch("{id:long}/status")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> UpdateStatus(long id, UpdateUserStatusRequestDto request, CancellationToken cancellationToken) =>
        (await service.UpdateStatusAsync(id, request, cancellationToken)).ToHttp(this);
}
