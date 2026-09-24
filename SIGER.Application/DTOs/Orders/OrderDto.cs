using SIGER.Domain.Enums;

namespace SIGER.Application.DTOs.Orders;

public sealed class OrderDto
{
    public long Id { get; set; }
    public long? TableId { get; set; }
    public int? TableNumber { get; set; }
    public long UserId { get; set; }
    public long? ClientId { get; set; }
    public DateTimeOffset OrderDateTime { get; set; }
    public OrderStatus Status { get; set; }
    public OrderOrigin Origin { get; set; }
    public OrderType Type { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }
    public bool AccountRequested { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
    public IReadOnlyCollection<OrderDetailDto> Details { get; set; } = Array.Empty<OrderDetailDto>();
}
