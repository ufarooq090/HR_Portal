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

    /// <summary>The local date part of <see cref="Format"/>: "Wed 15 Jan 2030".</summary>
    public static string FormatDate(DateTimeOffset moment, string timeZone) => Format(moment, timeZone).Split(", ", 2)[0];

    /// <summary>The local time part of <see cref="Format"/>, with the zone: "15:00 Asia/Karachi (UTC+05:00)".</summary>
    public static string FormatTime(DateTimeOffset moment, string timeZone) => Format(moment, timeZone).Split(", ", 2)[1];

    /// <summary>A calendar date as written in emails: "15 Jan 2030".</summary>
    public static string FormatDate(DateOnly date) => date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
