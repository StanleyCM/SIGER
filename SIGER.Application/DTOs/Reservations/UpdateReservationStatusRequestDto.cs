using SIGER.Domain.Enums;

namespace SIGER.Application.DTOs.Reservations;

public sealed class UpdateReservationStatusRequestDto
{
    public ReservationStatus Status { get; set; }
}
