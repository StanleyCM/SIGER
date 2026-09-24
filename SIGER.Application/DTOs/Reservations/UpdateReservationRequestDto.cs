namespace SIGER.Application.DTOs.Reservations;

public sealed class UpdateReservationRequestDto
{
    public long UserId { get; set; }
    public long TableId { get; set; }
    public DateTimeOffset ReservationDateTime { get; set; }
    public int NumberOfPeople { get; set; }
    public string? Notes { get; set; }
}
