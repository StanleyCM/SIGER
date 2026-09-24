using SIGER.Application.Base;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Application.Services;

public class KitchenService : IKitchenService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public KitchenService(IOrderRepository orderRepository, IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyCollection<OrderDto>>> GetKitchenOrdersAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.GetKitchenOrdersAsync(cancellationToken);
        return Result<IReadOnlyCollection<OrderDto>>.Success(orders.Select(Map).ToArray());
    }

    public Task<Result<OrderDto>> MarkInPreparationAsync(long orderId, CancellationToken cancellationToken = default)
        => ChangeStatusAsync(orderId, OrderStatus.InPreparation, cancellationToken);

    public Task<Result<OrderDto>> MarkReadyAsync(long orderId, CancellationToken cancellationToken = default)
        => ChangeStatusAsync(orderId, OrderStatus.Ready, cancellationToken);

    private async Task<Result<OrderDto>> ChangeStatusAsync(long orderId, OrderStatus status, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetWithDetailsAsync(orderId, cancellationToken);
        if (order is null) return Result<OrderDto>.Failure("Order not found.");
        if (order.Status is OrderStatus.Paid or OrderStatus.Cancelled) return Result<OrderDto>.Failure("Paid or cancelled orders cannot be updated by kitchen.");
        order.Status = status;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        _orderRepository.Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Success(Map(order));
    }

    private static OrderDto Map(Order order) => new()
    {
        Id = order.Id, TableId = order.TableId, TableNumber = order.Table?.Number, UserId = order.UserId, ClientId = order.ClientId,
        OrderDateTime = order.OrderDateTime, Status = order.Status, Origin = order.Origin, Type = order.Type, Total = order.Total,
        Notes = order.Notes, AccountRequested = order.AccountRequested, UpdatedAt = order.UpdatedAt, Version = order.Version,
        Details = order.Details.Select(detail => new OrderDetailDto
        {
            Id = detail.Id, ProductId = detail.ProductId, ProductName = detail.Product?.Name ?? string.Empty,
            Quantity = detail.Quantity, UnitPrice = detail.UnitPrice, Subtotal = detail.Subtotal, Note = detail.Note
        }).ToArray()
    };
}
