using HrPortal.Domain.Common;

namespace HrPortal.Domain.Organisation;

/// <summary>
/// Per-country configuration. Country-specific rules (probation, notice period, and later leave,
/// holidays, templates, checklists) are read from here and never hard-coded.
/// </summary>
public sealed class CountrySettings
{
    public const int DefaultProbationMonths = 3;
    public const int DefaultNoticePeriodDays = 30;

    private CountrySettings()
    {
    }

    public CountrySettings(string countryCode, string name, string currencyCode)
    {
        CountryCode = countryCode;
        Name = name;
        CurrencyCode = currencyCode;
    }

    /// <summary>ISO 3166-1 alpha-2 code, e.g. PK, US.</summary>
    public string CountryCode { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>ISO 4217 default currency, e.g. PKR, USD.</summary>
    public string CurrencyCode { get; private set; } = null!;

    public int ProbationMonths { get; private set; } = DefaultProbationMonths;

    public int NoticePeriodDays { get; private set; } = DefaultNoticePeriodDays;

    public void UpdateEmploymentDefaults(int probationMonths, int noticePeriodDays)
    {
        if (probationMonths is < 0 or > 24)
        {
            throw new DomainValidationException(nameof(probationMonths), "Probation must be between 0 and 24 months.");
        }

        if (noticePeriodDays is < 0 or > 365)
        {
            throw new DomainValidationException(nameof(noticePeriodDays), "Notice period must be between 0 and 365 days.");
        }

        ProbationMonths = probationMonths;
        NoticePeriodDays = noticePeriodDays;
    }
}
