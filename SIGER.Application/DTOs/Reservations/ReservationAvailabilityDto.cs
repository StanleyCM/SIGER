using System.ComponentModel.DataAnnotations;

namespace SIGER.Application.DTOs.Reservations;

public sealed class ReservationAvailabilityRequestDto
{
    [Required] public DateOnly? Date { get; set; }
    [Range(1, 100)] public int NumberOfPeople { get; set; }
    // Excluding a reservation requires its own valid credential.
    [Range(1, long.MaxValue)] public long? ReservationId { get; set; }
}

public sealed record ReservationAvailabilityDto(string Time, bool Available);
