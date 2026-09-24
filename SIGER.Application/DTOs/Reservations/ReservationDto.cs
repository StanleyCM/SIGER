using SIGER.Domain.Enums;

namespace SIGER.Application.DTOs.Reservations;

public sealed class ReservationDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long TableId { get; set; }
    public int? TableNumber { get; set; }
    public DateTimeOffset ReservationDateTime { get; set; }
    public int NumberOfPeople { get; set; }
    public ReservationStatus Status { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
