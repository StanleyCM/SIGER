using Microsoft.EntityFrameworkCore;
using SIGER.Application.Base;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;
using SIGER.Infrastructure.Persistence;

namespace SIGER.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly SIGERDbContext _context;

    public OrderRepository(SIGERDbContext context) => _context = context;

    public Task<Order?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => _context.Orders.Include(order => order.Table).Include(order => order.User).Include(order => order.Client)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    public Task<Order?> GetWithDetailsAsync(long id, CancellationToken cancellationToken = default)
        => CompleteOrders().AsSplitQuery().FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    public Task<bool> HasActiveOrdersAsync(long tableId, long excludingOrderId, CancellationToken cancellationToken = default)
        => _context.Orders.AnyAsync(order => order.TableId == tableId && order.Id != excludingOrderId &&
            order.Status != OrderStatus.Paid && order.Status != OrderStatus.Cancelled, cancellationToken);

    public async Task<PaginatedResult<Order>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        long? tableId = null,
        OrderStatus? status = null,
        long? orderId = null,
        CancellationToken cancellationToken = default)
    {
        var filtered = _context.Orders.AsNoTracking().AsQueryable();
        if (startDate.HasValue) filtered = filtered.Where(order => order.OrderDateTime >= startDate.Value);
        if (endDate.HasValue) filtered = filtered.Where(order => order.OrderDateTime <= endDate.Value);
        if (tableId.HasValue) filtered = filtered.Where(order => order.TableId == tableId.Value);
        if (status.HasValue) filtered = filtered.Where(order => order.Status == status.Value);
        if (orderId.HasValue) filtered = filtered.Where(order => order.Id == orderId.Value);

        var totalCount = await filtered.CountAsync(cancellationToken);
        var ids = await filtered.OrderByDescending(order => order.OrderDateTime).ThenByDescending(order => order.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(order => order.Id).ToArrayAsync(cancellationToken);

        var orders = await _context.Orders.AsNoTracking()
            .Where(order => ids.Contains(order.Id))
            .Include(order => order.Table)
            .Include(order => order.User)
            .Include(order => order.Client)
            .Include(order => order.Details).ThenInclude(detail => detail.Product)
            .AsSplitQuery()
            .ToArrayAsync(cancellationToken);

        var positions = ids.Select((id, index) => new { id, index }).ToDictionary(item => item.id, item => item.index);
        var ordered = orders.OrderBy(order => positions[order.Id]).ToArray();
        return new PaginatedResult<Order>(ordered, totalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyCollection<Order>> GetKitchenOrdersAsync(CancellationToken cancellationToken = default)
        => await _context.Orders.AsNoTracking()
            .Where(order => order.Status == OrderStatus.Pending || order.Status == OrderStatus.InPreparation || order.Status == OrderStatus.Ready)
            .Include(order => order.Table)
            .Include(order => order.Details).ThenInclude(detail => detail.Product)
            .AsSplitQuery()
            .OrderBy(order => order.OrderDateTime)
            .ToArrayAsync(cancellationToken);

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
        => await _context.Orders.AddAsync(order, cancellationToken);

    public void Update(Order order) => _context.Orders.Update(order);

    private IQueryable<Order> CompleteOrders()
        => _context.Orders
            .Include(order => order.Table)
            .Include(order => order.User)
            .Include(order => order.Client)
            .Include(order => order.Details).ThenInclude(detail => detail.Product)
            .Include(order => order.Payments);
}
