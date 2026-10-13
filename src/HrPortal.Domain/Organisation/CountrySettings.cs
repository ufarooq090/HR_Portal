using HrPortal.Domain.Common;

namespace HrPortal.Domain.Organisation;

/// <summary>
/// Per-country configuration. Country-specific rules (probation, notice period, time zone, and later
/// leave, holidays, templates, checklists) are read from here and never hard-coded. Settings cascade
/// to office locations and grades, which may override some of them.
/// </summary>
public sealed class CountrySettings
{
    public const int DefaultProbationMonths = 3;
    public const int DefaultNoticePeriodDays = 30;

    private CountrySettings()
    {
    }

    public CountrySettings(string countryCode, string name, string currencyCode, string defaultTimeZone)
    {
        CountryCode = Guard.CountryCode(nameof(countryCode), countryCode);
        CurrencyCode = Guard.CurrencyCode(nameof(currencyCode), currencyCode);
        UpdateDetails(name, defaultTimeZone);
    }

    /// <summary>ISO 3166-1 alpha-2 code, e.g. PK, US.</summary>
    public string CountryCode { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>ISO 4217 default currency, e.g. PKR, USD. Fixed once created: compensation is stored in it.</summary>
    public string CurrencyCode { get; private set; } = null!;

    /// <summary>IANA time zone for offices in this country that do not set their own.</summary>
    public string DefaultTimeZone { get; private set; } = null!;

    public int ProbationMonths { get; private set; } = DefaultProbationMonths;

    public int NoticePeriodDays { get; private set; } = DefaultNoticePeriodDays;

    public void UpdateDetails(string name, string defaultTimeZone)
    {
        Name = Guard.RequiredText(nameof(name), name, 100);
        DefaultTimeZone = Guard.TimeZone(nameof(defaultTimeZone), defaultTimeZone);
    }

    public void UpdateEmploymentDefaults(int probationMonths, int noticePeriodDays)
    {
        ProbationMonths = EmploymentTerms.ValidProbation(nameof(probationMonths), probationMonths);
        NoticePeriodDays = EmploymentTerms.ValidNotice(nameof(noticePeriodDays), noticePeriodDays);
    }
}
