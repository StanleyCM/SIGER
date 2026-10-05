using SIGER.Domain.Enums;

namespace SIGER.Application.DTOs.Reservations;

public sealed class GuestReservationDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTimeOffset? AccessExpiresAt { get; set; }
    public DateTimeOffset ReservationDateTime { get; set; }
    public int NumberOfPeople { get; set; }
    public ReservationStatus Status { get; set; }
    public string? Notes { get; set; }
}

public sealed class GuestReservationCreatedDto
{
    public GuestReservationDto Reservation { get; set; } = new();
    public string Message { get; set; } = "Reserva recibida: pendiente";
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
    public override string ToString() => nameof(GuestReservationCreatedDto);
}
