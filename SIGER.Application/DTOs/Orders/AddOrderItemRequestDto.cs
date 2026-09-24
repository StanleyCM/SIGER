namespace SIGER.Application.DTOs.Orders;

public sealed class AddOrderItemRequestDto
{
    public long ProductId { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
}
