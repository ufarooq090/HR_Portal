namespace HrPortal.Domain.Common;

/// <summary>Calendar dates as people in a given office see them; effective dates take effect at local midnight.</summary>
public static class LocalCalendar
{
    /// <param name="timeZone">IANA time zone, e.g. Asia/Karachi.</param>
    public static DateOnly Today(DateTimeOffset now, string timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime);

    /// <summary>
    /// A moment as someone in <paramref name="timeZone"/> reads it, with the zone named so there is no doubt:
    /// "Wed 15 Jan 2030, 15:00 Asia/Karachi (UTC+05:00)". Used in emails, which cannot adapt to the reader.
    /// </summary>
    public static string Format(DateTimeOffset moment, string timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(moment, TimeZoneInfo.FindSystemTimeZoneById(timeZone));
        var offset = local.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $@"{local:ddd d MMM yyyy, HH:mm} {timeZone} (UTC{sign}{offset.Duration():hh\:mm})");
    }
}
