using SIGER.Domain.Enums;

namespace SIGER.Application.DTOs.Payments;

public sealed class ProcessPaymentRequestDto
{
    public const int MaxReferenceLength = 150;

    public long OrderId { get; set; }
    public long UserId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
}
