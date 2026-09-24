using SIGER.Domain.Enums;

namespace SIGER.Application.DTOs.Orders;

public sealed class CreateOrderRequestDto
{
    public long UserId { get; set; }
    public long? TableId { get; set; }
    public long? ClientId { get; set; }
    public OrderOrigin Origin { get; set; }
    public OrderType Type { get; set; }
    public string? Notes { get; set; }
    public ICollection<CreateOrderDetailRequestDto> Items { get; set; } = new List<CreateOrderDetailRequestDto>();
}
