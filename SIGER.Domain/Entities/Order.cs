using SIGER.Domain.Base;
using SIGER.Domain.Enums;

namespace SIGER.Domain.Entities;

public class Order : BaseEntity
{
    public long? TableId { get; set; }
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

    public Table? Table { get; set; }
    public User User { get; set; } = null!;
    public User? Client { get; set; }

    public ICollection<OrderDetail> Details { get; set; } = new List<OrderDetail>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
