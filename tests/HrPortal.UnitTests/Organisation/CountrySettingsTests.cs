using HrPortal.Domain.Common;
using HrPortal.Domain.Organisation;

namespace HrPortal.UnitTests.Organisation;

public class CountrySettingsTests
{
    [Fact]
    public void New_country_uses_default_probation_and_notice_period()
    {
        var country = new CountrySettings("PK", "Pakistan", "PKR", "Asia/Karachi");

        Assert.Equal(3, country.ProbationMonths);
        Assert.Equal(30, country.NoticePeriodDays);
    }

    [Fact]
    public void Employment_defaults_can_be_changed_per_country()
    {
        var country = new CountrySettings("US", "United States", "USD", "America/New_York");

        country.UpdateEmploymentDefaults(probationMonths: 6, noticePeriodDays: 14);

        Assert.Equal(6, country.ProbationMonths);
        Assert.Equal(14, country.NoticePeriodDays);
    }

    [Theory]
    [InlineData("Karachi")]
    [InlineData("Not/AZone")]
    [InlineData("")]
    public void MC_01_default_time_zone_must_be_an_IANA_zone(string timeZone)
    {
        var ex = Assert.Throws<DomainValidationException>(() => new CountrySettings("PK", "Pakistan", "PKR", timeZone));

        Assert.Equal("defaultTimeZone", ex.Field);
    }

    [Theory]
    [InlineData("P", "PKR", "countryCode")]
    [InlineData("PK1", "PKR", "countryCode")]
    [InlineData("PK", "RS", "currencyCode")]
    public void Country_and_currency_codes_are_validated(string country, string currency, string field)
    {
        var ex = Assert.Throws<DomainValidationException>(() => new CountrySettings(country, "Test", currency, "Asia/Karachi"));

        Assert.Equal(field, ex.Field);
    }

    [Theory]
    [InlineData(-1, 30, "probationMonths")]
    [InlineData(25, 30, "probationMonths")]
    [InlineData(3, -1, "noticePeriodDays")]
    [InlineData(3, 366, "noticePeriodDays")]
    public void Out_of_range_values_are_rejected(int probation, int notice, string field)
    {
        var country = new CountrySettings("PK", "Pakistan", "PKR", "Asia/Karachi");

        var ex = Assert.Throws<DomainValidationException>(() => country.UpdateEmploymentDefaults(probation, notice));

        Assert.Equal(field, ex.Field);
    }
}
