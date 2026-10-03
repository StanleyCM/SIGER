using SIGER.Domain.Enums;

namespace SIGER.Application.DTOs.Orders;

public sealed class PreOrderDto
{
    public long Id { get; set; }
    public long ReservationId { get; set; }
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public IReadOnlyCollection<PreOrderItemDto> Items { get; set; } = [];
}

public sealed record PreOrderItemDto(long ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal Subtotal, string? Notes);
