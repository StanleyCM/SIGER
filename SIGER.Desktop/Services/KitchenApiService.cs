using SIGER.Application.DTOs.Orders;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Services;

internal sealed class KitchenApiService(ApiClient api)
{
    internal async Task<IReadOnlyCollection<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        var orders = await api.SendAsync<IReadOnlyCollection<OrderDto>>(
            HttpMethod.Get,
            "api/v1/kitchen/orders",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return orders
            .Where(order => order is not null && IsKitchenQueueStatus(order.Status))
            .OrderBy(order => order.OrderDateTime)
            .ThenBy(order => order.Id)
            .ToArray();
    }

    internal Task<OrderDto> MarkInPreparationAsync(long orderId, OrderStatus? currentStatus = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(orderId, 0);
        if (currentStatus.HasValue && !CanStartPreparation(currentStatus.Value))
            throw new ApiException("Solo las órdenes pendientes pueden pasar a preparación.");

        return api.SendAsync<OrderDto>(
            HttpMethod.Patch,
            $"api/v1/kitchen/orders/{orderId}/in-preparation",
            cancellationToken: cancellationToken);
    }

    internal Task<OrderDto> MarkReadyAsync(long orderId, OrderStatus? currentStatus = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(orderId, 0);
        if (currentStatus.HasValue && !CanMarkReady(currentStatus.Value))
            throw new ApiException("Solo las órdenes en preparación pueden marcarse como listas.");

        return api.SendAsync<OrderDto>(
            HttpMethod.Patch,
            $"api/v1/kitchen/orders/{orderId}/ready",
            cancellationToken: cancellationToken);
    }

    internal static bool IsKitchenQueueStatus(OrderStatus status) =>
        status is OrderStatus.Pending or OrderStatus.InPreparation or OrderStatus.Ready;

    internal static bool CanStartPreparation(OrderStatus status) => status == OrderStatus.Pending;

    internal static bool CanMarkReady(OrderStatus status) => status == OrderStatus.InPreparation;
}
