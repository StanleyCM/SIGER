using SIGER.Infrastructure.Security;

namespace SIGER.Tests.Infrastructure;

public class GuestReservationTokenTests
{

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
