using SIGER.Domain.Base;

namespace SIGER.Domain.Entities;

public class Audit : BaseEntity
{
    public long? UserId { get; set; }

    public string Action { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;

    public long? EntityId { get; set; }

    public string? PreviousData { get; set; }
    public string? NewData { get; set; }

    public string? IpAddress { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public User? User { get; set; }
}
