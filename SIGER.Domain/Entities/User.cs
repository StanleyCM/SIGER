using SIGER.Domain.Base;

namespace SIGER.Domain.Entities;

public class User : BaseEntity
{
    public long RoleId { get; set; }
    public Guid AuthUserId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Role Role { get; set; } = null!;

    public ICollection<Order> CreatedOrders { get; set; } = new List<Order>();
    public ICollection<Order> ClientOrders { get; set; } = new List<Order>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<Audit> Audits { get; set; } = new List<Audit>();
}
