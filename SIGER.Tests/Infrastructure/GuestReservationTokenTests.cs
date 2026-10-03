using SIGER.Infrastructure.Security;

namespace SIGER.Tests.Infrastructure;

public class GuestReservationTokenTests
{
    [Fact]
    public void Credentials_have_256_random_bits_and_fixed_size_hashes()
    {
        var service = new GuestReservationTokenService();
        var tokens = Enumerable.Range(0, 1000).Select(_ => service.Generate()).ToArray();
        Assert.Equal(1000, tokens.Distinct().Count());
        Assert.All(tokens, token => { Assert.Equal(64, token.Length); Assert.All(token, c => Assert.True(char.IsAsciiHexDigit(c))); Assert.Equal(32, service.Hash(token).Length); });
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("invalid")] [InlineData("ffffffff")]
    public void Malformed_credentials_fail(string? token)
    {
        var service = new GuestReservationTokenService();
        Assert.False(service.Validate(token, new byte[32], DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Only_matching_hash_before_expiry_is_valid()
    {
        var s = new GuestReservationTokenService(); var t = s.Generate(); var hash = s.Hash(t); var now = DateTimeOffset.UtcNow;
        Assert.True(s.Validate(t, hash, now.AddSeconds(1), now));
        Assert.False(s.Validate(t, hash, now, now));
        Assert.False(s.Validate(t, hash, now.AddSeconds(-1), now));
        Assert.False(s.Validate(s.Generate(), hash, now.AddHours(1), now));
        Assert.False(s.Validate(t, null, now.AddHours(1), now));
        Assert.False(s.Validate(t, hash, null, now));
    }
}
