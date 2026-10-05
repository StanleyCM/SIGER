using Moq;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.Exceptions;
using SIGER.Application.Services;
using SIGER.Domain.Entities;
using SIGER.Domain.Enums;

namespace SIGER.Tests.Application;

public class ReservationAvailabilityTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-05T17:00:00Z");
    }
    private static GuestReservationService Service(GuestFixture g) => new(g.F.Reservations.Object,
        g.F.Tables.Object, g.F.Orders.Object, g.Tokens, g.F.Work.Object, new FixedClock());
    private static ReservationAvailabilityRequestDto Request(int people = 2, int day = 6) => new()
    { Date = new DateOnly(2026, 10, day), NumberOfPeople = people };

    [Fact]
    public async Task Available_slots_use_Dominican_offset_and_read_only_shared_two_hour_checks()
    {
        var g = new GuestFixture(); var starts = new List<DateTimeOffset>();
        g.F.Reservations.Setup(x => x.HasOverlapAsync(4, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((long _, DateTimeOffset start, DateTimeOffset end, long? _, CancellationToken _) =>
            { Assert.Equal(TimeSpan.Zero, start.Offset); Assert.Equal(TimeSpan.FromHours(2), end - start); starts.Add(start); return false; });
        var result = await Service(g).GetAvailabilityAsync(Request());
        Assert.True(result.IsSuccess); Assert.Equal(13, result.Value!.Count); Assert.All(result.Value, s => Assert.True(s.Available));
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T16:00:00Z"), starts[0]);
        Assert.Equal("12:00", result.Value[0].Time); Assert.Equal("21:30", result.Value[^1].Time);
        g.F.NoSave(); g.F.Tables.Verify(x => x.GetByIdForUpdateAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        g.F.Tables.Verify(x => x.GetByIdAsync(4, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("full")] [InlineData("capacity")] [InlineData("out")] [InlineData("none")]
    public async Task Ineligible_tables_make_all_slots_unavailable(string reason)
    {
        var g = new GuestFixture();
        if (reason == "capacity") g.F.Table.Capacity = 1;
        if (reason == "out") g.F.Table.Status = TableStatus.OutOfService;
        if (reason == "none") g.F.Tables.Setup(x => x.GetReservationCandidateIdsAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        if (reason == "full") g.F.Reservations.Setup(x => x.HasOverlapAsync(4, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.All((await Service(g).GetAvailabilityAsync(Request())).Value!, s => Assert.False(s.Available));
        g.F.NoSave();
    }

    [Fact]
    public async Task People_date_and_past_slots_change_availability()
    {
        var g = new GuestFixture(); var service = Service(g);
        g.F.Reservations.Setup(x => x.HasOverlapAsync(4, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((long _, DateTimeOffset start, DateTimeOffset _, long? _, CancellationToken _) => start.ToOffset(TimeSpan.FromHours(-4)).Day == 6);
        Assert.All((await service.GetAvailabilityAsync(Request())).Value!, s => Assert.False(s.Available));
        Assert.All((await service.GetAvailabilityAsync(Request(day: 7))).Value!, s => Assert.True(s.Available));
        Assert.All((await service.GetAvailabilityAsync(Request(people: 5, day: 7))).Value!, s => Assert.False(s.Available));
        var today = (await service.GetAvailabilityAsync(Request(day: 5))).Value!;
        Assert.False(today.Single(s => s.Time == "13:00").Available);
        Assert.True(today.Single(s => s.Time == "13:30").Available);
    }

    [Fact]
    public async Task Own_reservation_is_excluded_only_with_its_valid_credential()
    {
        var g = new GuestFixture(); g.Reservation.AccessTokenExpiresAt = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
        var request = Request(); request.ReservationId = 10;
        await Assert.ThrowsAsync<UnauthorizedException>(() => Service(g).GetAvailabilityAsync(request));
        await Assert.ThrowsAsync<UnauthorizedException>(() => Service(g).GetAvailabilityAsync(request, "wrong"));
        Assert.True((await Service(g).GetAvailabilityAsync(request, g.Credential)).IsSuccess);
        g.F.Reservations.Verify(x => x.HasOverlapAsync(4, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), 10, It.IsAny<CancellationToken>()), Times.Exactly(13));
        g.F.NoSave();
    }

    [Theory]
    [InlineData(0, 6)] [InlineData(101, 6)] [InlineData(2, 4)]
    public async Task Invalid_query_is_rejected_before_table_reads(int people, int day)
    {
        var g = new GuestFixture(); Assert.True((await Service(g).GetAvailabilityAsync(Request(people, day))).IsFailure);
        g.F.Tables.Verify(x => x.GetReservationCandidateIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Today_uses_Dominican_date_and_accepts_day_30_only()
    {
        var g = new GuestFixture(); var query = Request(); query.Date = new DateOnly(2026, 11, 4);
        Assert.True((await Service(g).GetAvailabilityAsync(query)).IsSuccess);
        query.Date = query.Date.Value.AddDays(1); Assert.True((await Service(g).GetAvailabilityAsync(query)).IsFailure);
        query.Date = null; Assert.True((await Service(g).GetAvailabilityAsync(query)).IsFailure);
    }
}
