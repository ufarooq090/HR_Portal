using HrPortal.Domain.Common;

namespace HrPortal.Domain.Organisation;

/// <summary>An office. Inherits its country's settings and may override the time zone.</summary>
public sealed class OfficeLocation : Entity
{
    private OfficeLocation()
    {
    }

    public OfficeLocation(string countryCode, string name, string? city, string? timeZone)
    {
        CountryCode = Guard.CountryCode(nameof(countryCode), countryCode);
        Update(name, city, timeZone, isActive: true);
    }

    /// <summary>Fixed once created; employees at the office belong to this country.</summary>
    public string CountryCode { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? City { get; private set; }

    /// <summary>Overrides <see cref="CountrySettings.DefaultTimeZone"/> when set.</summary>
    public string? TimeZone { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string? city, string? timeZone, bool isActive)
    {
        Name = Guard.RequiredText(nameof(name), name, 200);
        City = Guard.OptionalText(nameof(city), city, 100);
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? null : Guard.TimeZone(nameof(timeZone), timeZone);
        IsActive = isActive;
    }

    public string EffectiveTimeZone(CountrySettings country)
    {
        if (country.CountryCode != CountryCode)
        {
            throw new ArgumentException("Country does not match the office.", nameof(country));
        }

        return TimeZone ?? country.DefaultTimeZone;
    }
}
