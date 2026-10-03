namespace SIGER.Application.Interfaces.Services;

public interface IGuestReservationTokenService
{
    string Generate();
    byte[] Hash(string token);
    bool Validate(string? token, byte[]? hash, DateTimeOffset? expiresAt, DateTimeOffset now);
}
