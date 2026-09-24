using SIGER.Application.Base;
using SIGER.Application.DTOs.Reservations;
using SIGER.Domain.Enums;

namespace SIGER.Application.Interfaces.Services;

public interface IReservationService
{
    Task<Result<ReservationDto>> CreateAsync(CreateReservationRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<ReservationDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Result<PaginatedResult<ReservationDto>>> GetPagedAsync(int pageNumber, int pageSize, long? userId = null, long? tableId = null, ReservationStatus? status = null, CancellationToken cancellationToken = default);
    Task<Result<ReservationDto>> UpdateAsync(long id, UpdateReservationRequestDto request, CancellationToken cancellationToken = default);
    Task<Result> ChangeStatusAsync(long id, UpdateReservationStatusRequestDto request, CancellationToken cancellationToken = default);
}
