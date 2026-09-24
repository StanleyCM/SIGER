namespace SIGER.Application.DTOs.Orders;

public sealed class UpdateOrderItemRequestDto
{
    public long OrderDetailId { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
}
