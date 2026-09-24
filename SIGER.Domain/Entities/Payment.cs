using SIGER.Domain.Base;
using SIGER.Domain.Enums;

namespace SIGER.Domain.Entities;

public class Payment : BaseEntity
{
    public long OrderId { get; set; }
    public long UserId { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }

    public string? Reference { get; set; }

    public DateTimeOffset PaymentDate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Order Order { get; set; } = null!;
    public User User { get; set; } = null!;
}
