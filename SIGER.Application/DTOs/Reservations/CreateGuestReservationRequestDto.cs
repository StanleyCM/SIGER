using System.ComponentModel.DataAnnotations;

namespace SIGER.Application.DTOs.Reservations;

public sealed class CreateGuestReservationRequestDto
{
    [Required, StringLength(150)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(30)] public string Phone { get; set; } = string.Empty;
    [EmailAddress, StringLength(150)] public string? Email { get; set; }
    public DateTimeOffset ReservationDateTime { get; set; }
    [Range(1, 100)] public int NumberOfPeople { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
}
