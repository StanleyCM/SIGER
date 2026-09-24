using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIGER.API.Authorization.Policies;
using SIGER.API.Extensions;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Interfaces.Services;

namespace SIGER.API.Controllers;

[ApiController]
[Route("api/v1/reservations")]
[Authorize(Policy = ApiPolicies.Reservations)]
public sealed class ReservationsController(IReservationService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<ReservationDto>), 200)]
    public async Task<IActionResult> GetPaged([Range(1, int.MaxValue)] int pageNumber = 1, [Range(1, 200)] int pageSize = 20,
        long? userId = null, long? tableId = null,
        [EnumDataType(typeof(SIGER.Domain.Enums.ReservationStatus))] SIGER.Domain.Enums.ReservationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        if (User.IsInRole(SigerClaims.Client)) userId = User.GetLocalUserId();
        return (await service.GetPagedAsync(pageNumber, pageSize, userId, tableId, status, cancellationToken)).ToHttp(this);
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ReservationDto), 200)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken);
        if (result.IsSuccess && !CanAccess(result.Value!)) return Forbid();
        return result.ToHttp(this);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ReservationDto), 201)]
    public async Task<IActionResult> Create(CreateReservationRequestDto request, CancellationToken cancellationToken)
    {
        if (User.IsInRole(SigerClaims.Client)) request.UserId = User.GetLocalUserId();
        return (await service.CreateAsync(request, cancellationToken)).ToHttp(this, nameof(GetById), value => new { id = value.Id });
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(ReservationDto), 200)]
    public async Task<IActionResult> Update(long id, UpdateReservationRequestDto request, CancellationToken cancellationToken)
    {
        var existing = await service.GetByIdAsync(id, cancellationToken);
        if (existing.IsFailure) return existing.ToHttp(this);
        if (!CanAccess(existing.Value!)) return Forbid();
        if (User.IsInRole(SigerClaims.Client)) request.UserId = User.GetLocalUserId();
        return (await service.UpdateAsync(id, request, cancellationToken)).ToHttp(this);
    }

    [HttpPatch("{id:long}/status")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ChangeStatus(long id, UpdateReservationStatusRequestDto request, CancellationToken cancellationToken)
    {
        var existing = await service.GetByIdAsync(id, cancellationToken);
        if (existing.IsFailure) return existing.ToHttp(this);
        if (!CanAccess(existing.Value!)) return Forbid();
        if (User.IsInRole(SigerClaims.Client) && request.Status != SIGER.Domain.Enums.ReservationStatus.Cancelled)
            return Forbid();
        return (await service.ChangeStatusAsync(id, request, cancellationToken)).ToHttp(this);
    }

    private bool CanAccess(ReservationDto reservation) =>
        !User.IsInRole(SigerClaims.Client) || reservation.UserId == User.GetLocalUserId();
}
