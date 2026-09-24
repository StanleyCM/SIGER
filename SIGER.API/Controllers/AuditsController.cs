using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIGER.API.Authorization.Policies;
using SIGER.API.Extensions;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Audits;
using SIGER.Application.Interfaces.Services;

namespace SIGER.API.Controllers;

[ApiController]
[Route("api/v1/audits")]
[Authorize(Policy = ApiPolicies.AdministratorOnly)]
public sealed class AuditsController(IAuditService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<AuditDto>), 200)]
    public async Task<IActionResult> GetPaged([Range(1, int.MaxValue)] int pageNumber = 1, [Range(1, 200)] int pageSize = 20,
        long? userId = null, string? entity = null, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null,
        CancellationToken cancellationToken = default) =>
        (await service.GetPagedAsync(pageNumber, pageSize, userId, entity, startDate?.ToUniversalTime(), endDate?.ToUniversalTime(), cancellationToken)).ToHttp(this);

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(AuditDto), 200)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken) =>
        (await service.GetByIdAsync(id, cancellationToken)).ToHttp(this);
}
