using SIGER.Application.Base;
using SIGER.Application.DTOs.Payments;

namespace SIGER.Application.Interfaces.Services;

public interface IPaymentService
{
    Task<Result<PaymentDto>> ProcessPaymentAsync(ProcessPaymentRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<PaymentDto>> GetByOrderIdAsync(long orderId, CancellationToken cancellationToken = default);
}
