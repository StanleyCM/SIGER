using SIGER.Domain.Base;
using SIGER.Domain.Enums;

namespace SIGER.Domain.Entities;

public class Reservation : BaseEntity
{
    public long? UserId { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public byte[]? AccessTokenHash { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public long TableId { get; set; }

    public DateTimeOffset ReservationDateTime { get; set; }

    public int NumberOfPeople { get; set; }

    public ReservationStatus Status { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
    public Order? Order { get; set; }
    public Table Table { get; set; } = null!;
}
