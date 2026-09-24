using SIGER.Application.Base;
using SIGER.Application.DTOs.Orders;

namespace SIGER.Application.Interfaces.Services;

public interface IKitchenService
{
    Task<Result<IReadOnlyCollection<OrderDto>>> GetKitchenOrdersAsync(CancellationToken cancellationToken = default);
    Task<Result<OrderDto>> MarkInPreparationAsync(long orderId, CancellationToken cancellationToken = default);
    Task<Result<OrderDto>> MarkReadyAsync(long orderId, CancellationToken cancellationToken = default);
}
