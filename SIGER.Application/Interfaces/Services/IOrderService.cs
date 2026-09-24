using SIGER.Application.Base;
using SIGER.Application.DTOs.Orders;
using SIGER.Domain.Enums;

namespace SIGER.Application.Interfaces.Services;

public interface IOrderService
{
    Task<Result<OrderDto>> CreateOrderAsync(CreateOrderRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<OrderDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Result<PaginatedResult<OrderDto>>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        long? tableId = null,
        OrderStatus? status = null,
        long? orderId = null,
        CancellationToken cancellationToken = default);
    Task<Result<OrderDto>> AddItemAsync(long orderId, AddOrderItemRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<OrderDto>> UpdateItemAsync(long orderId, UpdateOrderItemRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<OrderDto>> RemoveItemAsync(long orderId, long orderDetailId, CancellationToken cancellationToken = default);
    Task<Result<OrderDto>> UpdateStatusAsync(long orderId, UpdateOrderStatusRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<OrderDto>> RequestAccountAsync(long orderId, RequestAccountDto request, CancellationToken cancellationToken = default);
}
