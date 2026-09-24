using SIGER.Application.Base;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.Interfaces.Persistence;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;
using SIGER.Domain.Exceptions;

namespace SIGER.Application.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly ITableRepository _tableRepository;
    private readonly IProductRepository _productRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public OrderService(
        IOrderRepository orderRepository,
        ITableRepository tableRepository,
        IProductRepository productRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _tableRepository = tableRepository;
        _productRepository = productRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<OrderDto>> CreateOrderAsync(CreateOrderRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null || !user.IsActive) return Result<OrderDto>.Failure("The creator user does not exist or is inactive.");

        User? client = null;
        if (request.ClientId.HasValue)
        {
            client = await _userRepository.GetByIdAsync(request.ClientId.Value, cancellationToken);
            if (client is null) return Result<OrderDto>.Failure("Client not found.");
        }

        Table? table = null;
        if (request.TableId.HasValue)
        {
            table = await _tableRepository.GetByIdAsync(request.TableId.Value, cancellationToken);
            if (table is null) return Result<OrderDto>.Failure("Table not found.");
            if (table.Status != TableStatus.Available) return Result<OrderDto>.Failure("The selected table is not available.");
        }
        else if (request.Type == OrderType.Table)
        {
            return Result<OrderDto>.Failure("A table order requires a table.");
        }

        if (request.Items.Count == 0) return Result<OrderDto>.Failure("The order must contain at least one product.");

        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            TableId = table?.Id, Table = table, UserId = user.Id, User = user, ClientId = client?.Id, Client = client,
            OrderDateTime = now, UpdatedAt = now, Status = OrderStatus.Pending, Origin = request.Origin,
            Type = request.Type, Notes = Normalize(request.Notes), AccountRequested = false, Version = 0
        };

        foreach (var itemRequest in request.Items)
        {
            if (itemRequest.Quantity <= 0) return Result<OrderDto>.Failure("All product quantities must be greater than zero.");
            var product = await _productRepository.GetByIdAsync(itemRequest.ProductId, cancellationToken);
            if (product is null || !product.IsAvailable) return Result<OrderDto>.Failure($"Product {itemRequest.ProductId} does not exist or is unavailable.");
            order.Details.Add(CreateDetail(order, product, itemRequest.Quantity, itemRequest.Note));
        }

        RecalculateTotal(order);
        await _unitOfWork.ExecuteInTransactionAsync(async transactionToken =>
        {
            if (table is not null)
            {
                table.Status = TableStatus.Occupied;
                table.UpdatedAt = now;
                _tableRepository.Update(table);
            }
            await _orderRepository.AddAsync(order, transactionToken);
            await _unitOfWork.SaveChangesAsync(transactionToken);
        }, cancellationToken);

        return Result<OrderDto>.Success(Map(order));
    }

    public async Task<Result<OrderDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetWithDetailsAsync(id, cancellationToken);
        return order is null ? Result<OrderDto>.Failure("Order not found.") : Result<OrderDto>.Success(Map(order));
    }

    public async Task<Result<PaginatedResult<OrderDto>>> GetPagedAsync(int pageNumber, int pageSize, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null, long? tableId = null, OrderStatus? status = null, long? orderId = null, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1) return Result<PaginatedResult<OrderDto>>.Failure("Page number and page size must be greater than zero.");
        if (startDate.HasValue && endDate.HasValue && endDate < startDate) return Result<PaginatedResult<OrderDto>>.Failure("End date cannot be earlier than start date.");
        var page = await _orderRepository.GetPagedAsync(pageNumber, pageSize, startDate, endDate, tableId, status, orderId, cancellationToken);
        return Result<PaginatedResult<OrderDto>>.Success(new(page.Items.Select(Map), page.TotalCount, page.PageNumber, page.PageSize));
    }

    public async Task<Result<OrderDto>> AddItemAsync(long orderId, AddOrderItemRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0) return Result<OrderDto>.Failure("Quantity must be greater than zero.");
        var order = await _orderRepository.GetWithDetailsAsync(orderId, cancellationToken);
        var stateError = ValidateEditable(order);
        if (stateError is not null) return Result<OrderDto>.Failure(stateError);
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        if (product is null || !product.IsAvailable) return Result<OrderDto>.Failure("Product does not exist or is unavailable.");
        order!.Details.Add(CreateDetail(order, product, request.Quantity, request.Note));
        RecalculateTotal(order);
        order.UpdatedAt = DateTimeOffset.UtcNow;
        _orderRepository.Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Success(Map(order));
    }

    public async Task<Result<OrderDto>> UpdateItemAsync(long orderId, UpdateOrderItemRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0) return Result<OrderDto>.Failure("Quantity must be greater than zero.");
        var order = await _orderRepository.GetWithDetailsAsync(orderId, cancellationToken);
        var stateError = ValidateEditable(order);
        if (stateError is not null) return Result<OrderDto>.Failure(stateError);
        var detail = order!.Details.FirstOrDefault(item => item.Id == request.OrderDetailId);
        if (detail is null) return Result<OrderDto>.Failure("Order item not found.");
        var product = await _productRepository.GetByIdAsync(detail.ProductId, cancellationToken);
        if (product is null || !product.IsAvailable) return Result<OrderDto>.Failure("Product does not exist or is unavailable.");
        detail.Product = product;
        detail.Quantity = request.Quantity;
        detail.UnitPrice = product.Price;
        detail.Subtotal = product.Price * request.Quantity;
        detail.Note = Normalize(request.Note);
        RecalculateTotal(order);
        order.UpdatedAt = DateTimeOffset.UtcNow;
        _orderRepository.Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Success(Map(order));
    }

    public async Task<Result<OrderDto>> RemoveItemAsync(long orderId, long orderDetailId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetWithDetailsAsync(orderId, cancellationToken);
        var stateError = ValidateEditable(order);
        if (stateError is not null) return Result<OrderDto>.Failure(stateError);
        var detail = order!.Details.FirstOrDefault(item => item.Id == orderDetailId);
        if (detail is null) return Result<OrderDto>.Failure("Order item not found.");
        order.Details.Remove(detail);
        RecalculateTotal(order);
        order.UpdatedAt = DateTimeOffset.UtcNow;
        _orderRepository.Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Success(Map(order));
    }

    public async Task<Result<OrderDto>> UpdateStatusAsync(long orderId, UpdateOrderStatusRequestDto request, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetWithDetailsAsync(orderId, cancellationToken);
        if (order is null) return Result<OrderDto>.Failure("Order not found.");
        var stateError = ValidateEditable(order);
        if (stateError is not null) return Result<OrderDto>.Failure(stateError);
        if (request.Status == OrderStatus.Paid) return Result<OrderDto>.Failure("An order can only be paid through the payment workflow.");
        if (order.Version != request.Version) throw new BusinessRuleException("The order changed. Reload it and try again.");
        order.Status = request.Status;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        _orderRepository.Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Success(Map(order));
    }

    public async Task<Result<OrderDto>> RequestAccountAsync(long orderId, RequestAccountDto request, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetWithDetailsAsync(orderId, cancellationToken);
        if (order is null) return Result<OrderDto>.Failure("Order not found.");
        if (order.Status is OrderStatus.Paid or OrderStatus.Cancelled) return Result<OrderDto>.Failure("A paid or cancelled order cannot request an account.");
        if (order.Version != request.Version) throw new BusinessRuleException("The order changed. Reload it and try again.");
        order.AccountRequested = request.AccountRequested;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        _orderRepository.Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Success(Map(order));
    }

    private static string? ValidateEditable(Order? order)
        => order is null ? "Order not found." : order.Status is OrderStatus.Paid or OrderStatus.Cancelled ? "Paid or cancelled orders cannot be modified." : null;

    private static OrderDetail CreateDetail(Order order, Product product, int quantity, string? note) => new()
    {
        OrderId = order.Id, Order = order, ProductId = product.Id, Product = product, Quantity = quantity,
        UnitPrice = product.Price, Subtotal = product.Price * quantity, Note = Normalize(note)
    };

    private static void RecalculateTotal(Order order) => order.Total = order.Details.Sum(detail => detail.Subtotal);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
