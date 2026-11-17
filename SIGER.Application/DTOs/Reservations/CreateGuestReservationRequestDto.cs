using System.ComponentModel.DataAnnotations;
using SIGER.Application.Services;

namespace SIGER.Application.DTOs.Reservations;

public sealed class CreateGuestReservationRequestDto
{
    [Required, StringLength(150)] public string Name { get; set; } = string.Empty;
    [Required, RegularExpression(GuestReservationSchedule.PhonePattern)] public string Phone { get; set; } = string.Empty;
    [RegularExpression(GuestReservationSchedule.EmailPattern), StringLength(150)] public string? Email { get; set; }
    public DateTimeOffset ReservationDateTime { get; set; }
    [Range(1, GuestReservationSchedule.MaxPeople)] public int NumberOfPeople { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
}
