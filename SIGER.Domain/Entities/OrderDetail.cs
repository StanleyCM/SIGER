using SIGER.Domain.Base;

namespace SIGER.Domain.Entities;

public class OrderDetail : BaseEntity
{
    public long OrderId { get; set; }
    public long ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }

    public string? Note { get; set; }

    public Order Order { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
