namespace HrPortal.Domain.Common;

/// <summary>Calendar dates as people in a given office see them; effective dates take effect at local midnight.</summary>
public static class LocalCalendar
{
    /// <param name="timeZone">IANA time zone, e.g. Asia/Karachi.</param>
    public static DateOnly Today(DateTimeOffset now, string timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime);
}
