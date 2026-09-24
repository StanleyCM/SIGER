using SIGER.Domain.Base;
using SIGER.Domain.Enums;

namespace SIGER.Domain.Entities;

public class Table : BaseEntity
{
    public int Number { get; set; }
    public int Capacity { get; set; }

    public TableStatus Status { get; set; }

    public string? Location { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
