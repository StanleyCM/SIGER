using SIGER.Domain.Enums;

namespace SIGER.Application.DTOs.Payments;

public sealed class PaymentDto
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public long UserId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset PaymentDate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
