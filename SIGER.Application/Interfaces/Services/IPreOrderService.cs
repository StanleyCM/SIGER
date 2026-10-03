using SIGER.Application.Base;
using SIGER.Application.DTOs.Orders;

namespace SIGER.Application.Interfaces.Services;

public interface IPreOrderService
{
    Task<Result<PreOrderDto>> CreateAsync(long reservationId, string? credential, CreatePreOrderRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<PreOrderDto>> GetAsync(long reservationId, string? credential, CancellationToken cancellationToken = default);
}
