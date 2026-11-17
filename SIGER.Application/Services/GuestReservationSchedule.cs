namespace SIGER.Application.Services;

internal static class GuestReservationSchedule
{
    internal const int DurationHours = 2;
    internal const int MaxAdvanceDays = 30;
    internal const int MaxPeople = 12;
    internal const string PhonePattern = @"^[0-9]{10,15}\z";
    internal const string EmailPattern = @"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+\z";
    internal static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");
    internal static readonly IReadOnlyList<TimeOnly> Slots =
        Enumerable.Range(0, 5).Select(i => new TimeOnly(12, 0).AddMinutes(i * 30))
        .Concat(Enumerable.Range(0, 8).Select(i => new TimeOnly(18, 0).AddMinutes(i * 30))).ToArray();

    internal static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone).DateTime);
    internal static bool IsBookableDate(DateOnly date, DateTimeOffset now)
    {
        var daysAhead = date.DayNumber - Today(now).DayNumber;
        return daysAhead is >= 0 and <= MaxAdvanceDays;
    }

    internal static bool IsBookableStart(DateTimeOffset start, DateTimeOffset now)
    {
        if (start <= now || start > DateTimeOffset.MaxValue.AddHours(-DurationHours)) return false;
        var local = TimeZoneInfo.ConvertTime(start, Zone);
        return IsBookableDate(DateOnly.FromDateTime(local.DateTime), now)
            && Slots.Contains(TimeOnly.FromDateTime(local.DateTime));
    }
    internal static DateTimeOffset Start(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
