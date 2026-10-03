using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SIGER.API.Extensions;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Interfaces.Services;

namespace SIGER.API.Controllers;

[ApiController, AllowAnonymous, Route("api/v1/public/reservations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicReservationsController(IGuestReservationService reservations, IPreOrderService preorders) : ControllerBase
{
    public const string CredentialHeader = "X-Reservation-Token";

    [HttpPost, EnableRateLimiting("PublicWrite"), RequestSizeLimit(16_384)]
    [ProducesResponseType(typeof(GuestReservationCreatedDto), 201)]
    public async Task<IActionResult> Create(CreateGuestReservationRequestDto request, CancellationToken cancellationToken)
        => (await reservations.CreateAsync(request, cancellationToken)).ToHttp(this, nameof(Get), r => new { id = r.Reservation.Id });

    [HttpGet("{id:long}"), EnableRateLimiting("Public")]
    [ProducesResponseType(typeof(GuestReservationDto), 200)]
    public async Task<IActionResult> Get(long id, [FromHeader(Name = CredentialHeader)] string? credential, CancellationToken cancellationToken)
        => (await reservations.GetAsync(id, credential, cancellationToken)).ToHttp(this);

    [HttpPost("{id:long}/preorder"), EnableRateLimiting("PublicWrite"), RequestSizeLimit(32_768)]
    [ProducesResponseType(typeof(PreOrderDto), 201)]
    public async Task<IActionResult> CreatePreOrder(long id, CreatePreOrderRequestDto request,
        [FromHeader(Name = CredentialHeader)] string? credential, CancellationToken cancellationToken)
        => (await preorders.CreateAsync(id, credential, request, cancellationToken)).ToHttp(this, nameof(GetPreOrder), _ => new { id });

    [HttpGet("{id:long}/preorder"), EnableRateLimiting("Public")]
    [ProducesResponseType(typeof(PreOrderDto), 200)]
    public async Task<IActionResult> GetPreOrder(long id, [FromHeader(Name = CredentialHeader)] string? credential, CancellationToken cancellationToken)
        => (await preorders.GetAsync(id, credential, cancellationToken)).ToHttp(this);
}
