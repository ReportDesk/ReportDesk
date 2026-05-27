namespace ReportSystem.Helpers;

public static class IndonesianTime
{
    private static readonly TimeZoneInfo TimeZone = ResolveTimeZone();

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone);

    public static DateTime ToLocal(DateTime utcDateTime)
    {
        var normalizedUtc = utcDateTime.Kind == DateTimeKind.Utc
            ? utcDateTime
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        return TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, TimeZone);
    }

    public static DateTime LocalDateStartToUtc(DateTime localDate)
    {
        var localStart = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localStart, TimeZone);
    }

    public static DateTime LocalDateEndToUtc(DateTime localDate)
    {
        var localEnd = DateTime.SpecifyKind(localDate.Date.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localEnd, TimeZone);
    }

    public static string Format(DateTime utcDateTime, string format)
    {
        return ToLocal(utcDateTime).ToString(format);
    }

    private static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var timeZoneId in new[] { "Asia/Jakarta", "SE Asia Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Asia/Jakarta",
            TimeSpan.FromHours(7),
            "Western Indonesia Time",
            "Western Indonesia Time");
    }
}
