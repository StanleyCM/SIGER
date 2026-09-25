namespace SIGER.Application.Interfaces.Persistence;

/// <summary>Trusted local identity and connection address; never supplied by a request DTO.</summary>
public interface IAuditActor
{
    long? UserId { get; }
    string? IpAddress { get; }
}
