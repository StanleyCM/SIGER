using SIGER.Domain.Base;
using SIGER.Domain.Enums;

namespace SIGER.Domain.Entities;

public class Reservation : BaseEntity
{
    public long UserId { get; set; }
    public long TableId { get; set; }

    public DateTimeOffset ReservationDateTime { get; set; }

    public int NumberOfPeople { get; set; }

    public ReservationStatus Status { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public Table Table { get; set; } = null!;
}
