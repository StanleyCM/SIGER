using System.Globalization;
using SIGER.Application.Base;
using SIGER.Application.DTOs.Orders;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Services;

internal sealed record OrderQueryFilter(
    int PageNumber = 1,
    int PageSize = 20,
    long? TableId = null,
    OrderStatus? Status = null,
    long? OrderId = null,
    DateTimeOffset? StartDate = null,
    DateTimeOffset? EndDate = null);

internal sealed class OrderApiService(ApiClient api)
{
    internal Task<PaginatedResult<OrderDto>> GetPagedAsync(OrderQueryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var query = filter ?? new OrderQueryFilter();
        if (query.PageNumber < 1 || query.PageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(filter), "La paginación de órdenes debe estar entre 1 y 200.");
        if (query.TableId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(filter), "El identificador de mesa debe ser positivo.");
        if (query.OrderId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(filter), "El identificador de orden debe ser positivo.");
        if (query.StartDate.HasValue && query.EndDate.HasValue && query.EndDate < query.StartDate)
            throw new ApiException("La fecha final no puede ser anterior a la fecha inicial.");

        var parameters = new List<string>(7)
        {
            $"pageNumber={query.PageNumber}",
            $"pageSize={query.PageSize}"
        };
        if (query.TableId.HasValue) parameters.Add($"tableId={query.TableId.Value}");
        if (query.Status.HasValue) parameters.Add($"status={Uri.EscapeDataString(query.Status.Value.ToString())}");
        if (query.OrderId.HasValue) parameters.Add($"orderId={query.OrderId.Value}");
        if (query.StartDate.HasValue)
            parameters.Add($"startDate={Uri.EscapeDataString(query.StartDate.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}");
        if (query.EndDate.HasValue)
            parameters.Add($"endDate={Uri.EscapeDataString(query.EndDate.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}");

        var path = "api/v1/orders?" + string.Join('&', parameters);
        return api.SendAsync<PaginatedResult<OrderDto>>(HttpMethod.Get, path, cancellationToken: cancellationToken);
    }

    internal Task<OrderDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(id, 0);
        return api.SendAsync<OrderDto>(HttpMethod.Get, $"api/v1/orders/{id}", cancellationToken: cancellationToken);
    }

    internal Task<OrderDto> CreateTableOrderAsync(long tableId, IReadOnlyCollection<CreateOrderDetailRequestDto> items,
        string? notes = null, CancellationToken cancellationToken = default) =>
        CreateTableOrderAsync(tableId, items, notes, reservationId: null, cancellationToken);

    internal Task<OrderDto> CreateTableOrderAsync(long tableId, IReadOnlyCollection<CreateOrderDetailRequestDto> items,
        string? notes, long? reservationId, CancellationToken cancellationToken = default)
    {
        if (tableId <= 0)
            throw new ApiException("Selecciona una mesa válida para crear la orden.");
        if (reservationId is <= 0)
            throw new ApiException("El identificador de reservación debe ser positivo.");
        return CreateOrderAsync(OrderType.Table, tableId, reservationId, items, notes, cancellationToken);
    }

    internal Task<OrderDto> CreateTakeAwayOrderAsync(IReadOnlyCollection<CreateOrderDetailRequestDto> items,
        string? notes = null, CancellationToken cancellationToken = default) =>
        CreateOrderAsync(OrderType.TakeAway, null, null, items, notes, cancellationToken);

    private Task<OrderDto> CreateOrderAsync(OrderType type, long? tableId, long? reservationId,
        IReadOnlyCollection<CreateOrderDetailRequestDto> items, string? notes, CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
            throw new ApiException("Agrega al menos un producto a la orden.");
        if (items.Any(item => item is null || item.ProductId <= 0 || item.Quantity <= 0))
            throw new ApiException("Cada línea debe tener un producto válido y una cantidad mayor que cero.");

        var request = new CreateOrderRequestDto
        {
            ReservationId = reservationId,
            TableId = tableId,
            Origin = OrderOrigin.Desktop,
            Type = type,
            Notes = Normalize(notes),
            Items = items.Select(item => new CreateOrderDetailRequestDto
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                Note = Normalize(item.Note)
            }).ToList()
        };

        return api.SendAsync<OrderDto>(HttpMethod.Post, "api/v1/orders", request, cancellationToken: cancellationToken);
    }

    internal Task<OrderDto> AddItemAsync(long orderId, long productId, int quantity, string? note = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(orderId, 0);
        if (productId <= 0)
            throw new ApiException("Selecciona un producto válido para agregar a la orden.");
        if (quantity <= 0)
            throw new ApiException("La cantidad debe ser mayor que cero.");

        var request = new AddOrderItemRequestDto
        {
            ProductId = productId,
            Quantity = quantity,
            Note = Normalize(note)
        };
        return api.SendAsync<OrderDto>(HttpMethod.Post, $"api/v1/orders/{orderId}/items", request,
            cancellationToken: cancellationToken);
    }

    internal Task<OrderDto> UpdateItemAsync(long orderId, long orderDetailId, int quantity, string? note = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(orderId, 0);
        if (orderDetailId <= 0)
            throw new ApiException("Selecciona una línea válida de la orden para modificar.");
        if (quantity <= 0)
            throw new ApiException("La cantidad debe ser mayor que cero.");

        var request = new UpdateOrderItemRequestDto
        {
            OrderDetailId = orderDetailId,
            Quantity = quantity,
            Note = Normalize(note)
        };
        return api.SendAsync<OrderDto>(HttpMethod.Put, $"api/v1/orders/{orderId}/items/{orderDetailId}", request,
            cancellationToken: cancellationToken);
    }

    internal Task<OrderDto> RemoveItemAsync(long orderId, long orderDetailId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(orderId, 0);
        if (orderDetailId <= 0)
            throw new ApiException("Selecciona una línea válida de la orden para eliminar.");

        return api.SendAsync<OrderDto>(HttpMethod.Delete, $"api/v1/orders/{orderId}/items/{orderDetailId}",
            cancellationToken: cancellationToken);
    }

    internal Task<OrderDto> ChangeStatusAsync(long orderId, OrderStatus status, long version,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(orderId, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        if (status is not (OrderStatus.Served or OrderStatus.Cancelled))
            throw new ApiException("El mesero solo puede marcar una orden como servida o cancelarla.");

        var request = new UpdateOrderStatusRequestDto
        {
            Status = status,
            Version = version
        };
        return api.SendAsync<OrderDto>(HttpMethod.Patch, $"api/v1/orders/{orderId}/status", request,
            cancellationToken: cancellationToken);
    }

    internal Task<OrderDto> RequestAccountAsync(long orderId, long version, bool accountRequested = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(orderId, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(version);

        var request = new RequestAccountDto
        {
            AccountRequested = accountRequested,
            Version = version
        };
        return api.SendAsync<OrderDto>(HttpMethod.Post, $"api/v1/orders/{orderId}/request-account", request,
            cancellationToken: cancellationToken);
    }

    internal static bool CanEditItems(OrderStatus status) =>
        status is OrderStatus.Pending or OrderStatus.InPreparation or OrderStatus.Ready or OrderStatus.Served;

    internal static bool CanMarkServed(OrderStatus status) => status == OrderStatus.Ready;

    internal static bool CanCancel(OrderStatus status) => CanEditItems(status);

    internal static bool CanRequestAccount(OrderDto order) =>
        order is not null && CanEditItems(order.Status) && !order.AccountRequested;

    internal static bool IsActiveOrder(OrderStatus status) => CanEditItems(status);

    internal static string FormatStatus(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Pendiente",
        OrderStatus.InPreparation => "En preparación",
        OrderStatus.Ready => "Lista para servir",
        OrderStatus.Served => "Servida",
        OrderStatus.Paid => "Pagada",
        OrderStatus.Cancelled => "Cancelada",
        OrderStatus.PreOrdered => "Preordenada",
        _ => status.ToString()
    };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
