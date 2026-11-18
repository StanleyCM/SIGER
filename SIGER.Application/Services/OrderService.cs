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
    private readonly IReservationRepository _reservationRepository;
    private readonly TimeProvider _clock;

    public OrderService(
        IOrderRepository orderRepository,
        ITableRepository tableRepository,
        IProductRepository productRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IReservationRepository reservationRepository,
        TimeProvider? clock = null)
    {
        _orderRepository = orderRepository;
        _tableRepository = tableRepository;
        _productRepository = productRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _reservationRepository = reservationRepository;
        _clock = clock ?? TimeProvider.System;
    }

    public Task<Result<OrderDto>> CreateOrderAsync(CreateOrderRequestDto request, CancellationToken cancellationToken = default)
        => _unitOfWork.ExecuteInTransactionAsync(token => CreateOrderInTransactionAsync(request, token), cancellationToken);

    private async Task<Result<OrderDto>> CreateOrderInTransactionAsync(CreateOrderRequestDto request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null || !user.IsActive) return Result<OrderDto>.Failure("The creator user does not exist or is inactive.");

        User? client = null;
        if (request.ClientId.HasValue)
        {
            client = await _userRepository.GetByIdAsync(request.ClientId.Value, cancellationToken);
            if (client is null) return Result<OrderDto>.Failure("Client not found.");
        }

        Reservation? reservation = null;
        if (request.ReservationId.HasValue)
        {
            if (request.ReservationId.Value <= 0) return Result<OrderDto>.Failure("Reservation not found.");
            reservation = await _reservationRepository.GetByIdForUpdateAsync(request.ReservationId.Value, cancellationToken);
            if (reservation is null) return Result<OrderDto>.Failure("Reservation not found.");
            if (reservation.Status == ReservationStatus.Cancelled)
                return Result<OrderDto>.Failure("A cancelled reservation cannot be received or activated.");
            if (request.Type != OrderType.Table)
                return Result<OrderDto>.Failure("A reservation requires a table order.");
            if (request.TableId.HasValue && reservation.TableId != request.TableId.Value)
                return Result<OrderDto>.Failure("The order table must match the reservation table.");
            if (await _orderRepository.GetByReservationIdAsync(request.ReservationId.Value, cancellationToken) is not null)
                return Result<OrderDto>.Failure("An order already exists for this reservation.");
        }

        Table? table = null;
        var now = _clock.GetUtcNow();
        if ((reservation?.TableId ?? request.TableId) is { } tableId)
        {
            table = await _tableRepository.GetByIdForUpdateAsync(tableId, cancellationToken);
            if (table is null) return Result<OrderDto>.Failure("Table not found.");
            if (!(table.Status == TableStatus.Available || (reservation is not null && table.Status == TableStatus.Reserved)) ||
                await _orderRepository.HasActiveOrdersAsync(table.Id, 0, cancellationToken))
                return Result<OrderDto>.Failure("The selected table is not available.");
            now = _clock.GetUtcNow();
            if (reservation is not null)
            {
                var arrivalError = ReservationService.ValidateArrivalWindow(reservation.ReservationDateTime, now);
                if (arrivalError is not null) return Result<OrderDto>.Failure(arrivalError);
                if (await ReservationService.HasArrivalOverlapAsync(_reservationRepository, reservation, now, cancellationToken))
                    return Result<OrderDto>.Failure(ReservationService.ArrivalOverlap);
            }
            else if (await _reservationRepository.HasReceivedPartyAsync(table.Id, now, cancellationToken))
                return Result<OrderDto>.Failure("The selected table is not available.");
        }
        else if (request.Type == OrderType.Table)
        {
            return Result<OrderDto>.Failure("A table order requires a table.");
        }

        if (request.Items.Count == 0) return Result<OrderDto>.Failure("The order must contain at least one product.");

        var order = new Order
        {
            ReservationId = request.ReservationId, Reservation = reservation,
            TableId = table?.Id, Table = table, UserId = user.Id, User = user, ClientId = client?.Id, Client = client,
            OrderDateTime = now, UpdatedAt = now, Status = OrderStatus.Pending, Origin = request.Origin,
            Type = request.Type, Notes = Normalize(request.Notes), AccountRequested = false, Version = 0
        };

        if (request.Items.Any(item => item.Quantity <= 0))
            return Result<OrderDto>.Failure("All product quantities must be greater than zero.");
        var products = (await _productRepository.GetByIdsAsync(
            request.Items.Select(item => item.ProductId).Distinct().ToArray(), cancellationToken)).ToDictionary(product => product.Id);
        foreach (var itemRequest in request.Items)
        {
            products.TryGetValue(itemRequest.ProductId, out var product);
            if (product is null || !product.IsAvailable) return Result<OrderDto>.Failure($"Product {itemRequest.ProductId} does not exist or is unavailable.");
            order.Details.Add(CreateDetail(order, product, itemRequest.Quantity, itemRequest.Note));
        }

        RecalculateTotal(order);
        if (reservation is not null && reservation.Status != ReservationStatus.Completed)
        {
            reservation.Status = ReservationStatus.Completed;
            reservation.UpdatedAt = now;
            _reservationRepository.Update(reservation);
        }
        if (table is not null)
        {
            table.Status = TableStatus.Occupied;
            table.UpdatedAt = now;
            _tableRepository.Update(table);
        }
        await _orderRepository.AddAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<OrderDto>.Success(Map(order));
    }

    public async Task<Result<OrderDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetWithDetailsAsync(id, cancellationToken);
        return order is null ? Result<OrderDto>.Failure("Order not found.") : Result<OrderDto>.Success(Map(order));
    }

    public async Task<Result<PaginatedResult<OrderDto>>> GetPagedAsync(int pageNumber, int pageSize, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null, long? tableId = null, OrderStatus? status = null, long? orderId = null, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > 200 || ((long)pageNumber - 1) * pageSize > int.MaxValue) return Result<PaginatedResult<OrderDto>>.Failure("Page number and size must be positive, size at most 200, and offset within the supported range.");
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
        detail.Subtotal = detail.UnitPrice * request.Quantity;
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

    public Task<Result<OrderDto>> UpdateStatusAsync(long orderId, UpdateOrderStatusRequestDto request, CancellationToken cancellationToken = default)
        => _unitOfWork.ExecuteInTransactionAsync(token => UpdateStatusInTransactionAsync(orderId, request, token), cancellationToken);

    private async Task<Result<OrderDto>> UpdateStatusInTransactionAsync(long orderId, UpdateOrderStatusRequestDto request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetWithDetailsAsync(orderId, cancellationToken);
        if (order is null) return Result<OrderDto>.Failure("Order not found.");
        var stateError = ValidateEditable(order);
        if (stateError is not null) return Result<OrderDto>.Failure(stateError);
        if (request.Status == OrderStatus.Paid) return Result<OrderDto>.Failure("An order can only be paid through the payment workflow.");
        if (order.Version != request.Version) throw new BusinessRuleException("The order changed. Reload it and try again.");
        if (request.Status == OrderStatus.Served && order.Status != OrderStatus.Ready)
            return Result<OrderDto>.Failure("Only ready orders can be served.");
        if (request.Status is not (OrderStatus.Served or OrderStatus.Cancelled))
            return Result<OrderDto>.Failure("Use the kitchen workflow for preparation transitions.");
        if (request.Status == OrderStatus.Cancelled && order.TableId.HasValue)
        {
            var table = await _tableRepository.GetByIdForUpdateAsync(order.TableId.Value, cancellationToken);
            if (table is null) return Result<OrderDto>.Failure("Table not found.");
            if (table.Status == TableStatus.Occupied && !await _orderRepository.HasActiveOrdersAsync(table.Id, order.Id, cancellationToken))
            {
                table.Status = TableStatus.Available;
                _tableRepository.Update(table);
            }
        }
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
        if (order.Status == OrderStatus.PreOrdered) return Result<OrderDto>.Failure("A preorder is not operational.");
        if (order.Status is OrderStatus.Paid or OrderStatus.Cancelled) return Result<OrderDto>.Failure("A paid or cancelled order cannot request an account.");
        if (order.Version != request.Version) throw new BusinessRuleException("The order changed. Reload it and try again.");
        order.AccountRequested = request.AccountRequested;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        _orderRepository.Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Success(Map(order));
    }

    private static string? ValidateEditable(Order? order)
        => order is null ? "Order not found." : order.Status == OrderStatus.PreOrdered ? "A preorder is not operational." : order.Status is OrderStatus.Paid or OrderStatus.Cancelled ? "Paid or cancelled orders cannot be modified." : null;

    private static OrderDetail CreateDetail(Order order, Product product, int quantity, string? note) => new()
    {
        OrderId = order.Id, Order = order, ProductId = product.Id, Product = product, Quantity = quantity,
        UnitPrice = product.Price, Subtotal = product.Price * quantity, Note = Normalize(note)
    };

    private static void RecalculateTotal(Order order) => order.Total = order.Details.Sum(detail => detail.Subtotal);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OrderDto Map(Order order) => new()
    {
        Id = order.Id, ReservationId = order.ReservationId, TableId = order.TableId, TableNumber = order.Table?.Number, UserId = order.UserId, ClientId = order.ClientId,
        OrderDateTime = order.OrderDateTime, Status = order.Status, Origin = order.Origin, Type = order.Type, Total = order.Total,
        Notes = order.Notes, AccountRequested = order.AccountRequested, UpdatedAt = order.UpdatedAt, Version = order.Version,
        Details = order.Details.Select(detail => new OrderDetailDto
        {
            Id = detail.Id, ProductId = detail.ProductId, ProductName = detail.Product?.Name ?? string.Empty,
            Quantity = detail.Quantity, UnitPrice = detail.UnitPrice, Subtotal = detail.Subtotal, Note = detail.Note
        }).ToArray()
    };
}
