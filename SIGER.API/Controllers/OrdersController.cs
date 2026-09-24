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
[Route("api/v1/orders")]
[Authorize(Policy = ApiPolicies.StaffOnly)]
public sealed class OrdersController(IOrderService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<OrderDto>), 200)]
    public async Task<IActionResult> GetPaged([Range(1, int.MaxValue)] int pageNumber = 1, [Range(1, 200)] int pageSize = 20,
        DateTimeOffset? startDate = null, DateTimeOffset? endDate = null, long? tableId = null,
        [EnumDataType(typeof(SIGER.Domain.Enums.OrderStatus))] SIGER.Domain.Enums.OrderStatus? status = null,
        long? orderId = null, CancellationToken cancellationToken = default) =>
        (await service.GetPagedAsync(pageNumber, pageSize, startDate?.ToUniversalTime(), endDate?.ToUniversalTime(),
            tableId, status, orderId, cancellationToken)).ToHttp(this);

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(OrderDto), 200)]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken) =>
        (await service.GetByIdAsync(id, cancellationToken)).ToHttp(this);

    [HttpPost]
    [Authorize(Policy = ApiPolicies.WaiterOrAdministrator)]
    [ProducesResponseType(typeof(OrderDto), 201)]
    public async Task<IActionResult> Create(CreateOrderRequestDto request, CancellationToken cancellationToken)
    {
        request.UserId = User.GetLocalUserId();
        return (await service.CreateOrderAsync(request, cancellationToken)).ToHttp(this, nameof(GetById), value => new { id = value.Id });
    }

    [HttpPost("{id:long}/items")]
    [Authorize(Policy = ApiPolicies.WaiterOrAdministrator)]
    [ProducesResponseType(typeof(OrderDto), 200)]
    public async Task<IActionResult> AddItem(long id, AddOrderItemRequestDto request, CancellationToken cancellationToken) =>
        (await service.AddItemAsync(id, request, cancellationToken)).ToHttp(this);

    [HttpPut("{id:long}/items/{itemId:long}")]
    [Authorize(Policy = ApiPolicies.WaiterOrAdministrator)]
    [ProducesResponseType(typeof(OrderDto), 200)]
    public async Task<IActionResult> UpdateItem(long id, long itemId, UpdateOrderItemRequestDto request, CancellationToken cancellationToken)
    {
        request.OrderDetailId = itemId;
        return (await service.UpdateItemAsync(id, request, cancellationToken)).ToHttp(this);
    }

    [HttpDelete("{id:long}/items/{itemId:long}")]
    [Authorize(Policy = ApiPolicies.WaiterOrAdministrator)]
    [ProducesResponseType(typeof(OrderDto), 200)]
    public async Task<IActionResult> RemoveItem(long id, long itemId, CancellationToken cancellationToken) =>
        (await service.RemoveItemAsync(id, itemId, cancellationToken)).ToHttp(this);

    [HttpPatch("{id:long}/status")]
    [Authorize(Policy = ApiPolicies.WaiterOrAdministrator)]
    [ProducesResponseType(typeof(OrderDto), 200)]
    public async Task<IActionResult> UpdateStatus(long id, UpdateOrderStatusRequestDto request, CancellationToken cancellationToken)
    {
        // Payment and kitchen transitions are authorized through their dedicated endpoints.
        if (request.Status is not (SIGER.Domain.Enums.OrderStatus.Served or SIGER.Domain.Enums.OrderStatus.Cancelled))
            return Forbid();
        return (await service.UpdateStatusAsync(id, request, cancellationToken)).ToHttp(this);
    }

    [HttpPost("{id:long}/request-account")]
    [Authorize(Policy = ApiPolicies.WaiterOrAdministrator)]
    [ProducesResponseType(typeof(OrderDto), 200)]
    public async Task<IActionResult> RequestAccount(long id, RequestAccountDto request, CancellationToken cancellationToken) =>
        (await service.RequestAccountAsync(id, request, cancellationToken)).ToHttp(this);
}
