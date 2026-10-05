using SIGER.Application.Base;
using SIGER.Application.DTOs.Reservations;

namespace SIGER.Application.Interfaces.Services;

public interface IGuestReservationService
{
    Task<Result<IReadOnlyList<ReservationAvailabilityDto>>> GetAvailabilityAsync(ReservationAvailabilityRequestDto request, string? credential = null, CancellationToken cancellationToken = default);
    Task<Result<GuestReservationCreatedDto>> CreateAsync(CreateGuestReservationRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<GuestReservationDto>> GetAsync(long id, string? credential, CancellationToken cancellationToken = default);
    Task<Result<GuestReservationDto>> UpdateAsync(long id, string? credential, UpdateGuestReservationRequestDto request, CancellationToken cancellationToken = default);
}
