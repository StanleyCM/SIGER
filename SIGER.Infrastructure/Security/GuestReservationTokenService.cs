using System.Security.Cryptography;
using System.Text;
using SIGER.Application.Interfaces.Services;

namespace SIGER.Infrastructure.Security;

public sealed class GuestReservationTokenService : IGuestReservationTokenService
{
    // 256 random bits encoded as 64 lowercase hexadecimal characters.
    public string Generate() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public bool Validate(string? token, byte[]? hash, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (token is null || token.Length != 64 || token.Any(c => !char.IsAsciiHexDigit(c))) return false;
        var matches = CryptographicOperations.FixedTimeEquals(Hash(token), hash ?? new byte[32]);
        return matches && hash?.Length == 32 && expiresAt.HasValue && expiresAt.Value > now;
    }
}
