using SIGER.Application.Base;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Application.Interfaces.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Order?> GetWithDetailsAsync(long id, CancellationToken cancellationToken = default);
    Task<PaginatedResult<Order>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        long? tableId = null,
        OrderStatus? status = null,
        long? orderId = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Order>> GetKitchenOrdersAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    void Update(Order order);
}
