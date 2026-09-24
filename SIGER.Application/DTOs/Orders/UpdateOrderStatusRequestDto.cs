using SIGER.Domain.Enums;

namespace SIGER.Application.DTOs.Orders;

public sealed class UpdateOrderStatusRequestDto
{
    public OrderStatus Status { get; set; }
    public long Version { get; set; }
}
