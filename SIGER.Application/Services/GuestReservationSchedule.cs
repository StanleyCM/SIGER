namespace SIGER.Application.Services;

internal static class GuestReservationSchedule
{
    internal const int DurationHours = 2;
    internal const int MaxAdvanceDays = 30;
    internal static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");
    internal static readonly IReadOnlyList<TimeOnly> Slots =
        Enumerable.Range(0, 5).Select(i => new TimeOnly(12, 0).AddMinutes(i * 30))
        .Concat(Enumerable.Range(0, 8).Select(i => new TimeOnly(18, 0).AddMinutes(i * 30))).ToArray();

    internal static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone).DateTime);
    internal static DateTimeOffset Start(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
