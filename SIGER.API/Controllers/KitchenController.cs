using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIGER.API.Authorization.Policies;
using SIGER.API.Extensions;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.Interfaces.Services;

namespace SIGER.API.Controllers;

[ApiController]
[Route("api/v1/kitchen")]
[Authorize(Policy = ApiPolicies.KitchenOrAdministrator)]
public sealed class KitchenController(IKitchenService service) : ControllerBase
{
    [HttpGet("orders")]
    [ProducesResponseType(typeof(IReadOnlyCollection<OrderDto>), 200)]
    public async Task<IActionResult> GetOrders(CancellationToken cancellationToken) =>
        (await service.GetKitchenOrdersAsync(cancellationToken)).ToHttp(this);

    [HttpPatch("orders/{id:long}/in-preparation")]
    [ProducesResponseType(typeof(OrderDto), 200)]
    public async Task<IActionResult> MarkInPreparation(long id, CancellationToken cancellationToken) =>
        (await service.MarkInPreparationAsync(id, cancellationToken)).ToHttp(this);

    [HttpPatch("orders/{id:long}/ready")]
    [ProducesResponseType(typeof(OrderDto), 200)]
    public async Task<IActionResult> MarkReady(long id, CancellationToken cancellationToken) =>
        (await service.MarkReadyAsync(id, cancellationToken)).ToHttp(this);
}
