using HrPortal.Domain.Common;
using HrPortal.Domain.Organisation;

namespace HrPortal.UnitTests.Organisation;

public class CountrySettingsTests
{
    [Fact]
    public void New_country_uses_default_probation_and_notice_period()
    {
        var country = new CountrySettings("PK", "Pakistan", "PKR");

        Assert.Equal(3, country.ProbationMonths);
        Assert.Equal(30, country.NoticePeriodDays);
    }

    [Fact]
    public void Employment_defaults_can_be_changed_per_country()
    {
        var country = new CountrySettings("US", "United States", "USD");

        country.UpdateEmploymentDefaults(probationMonths: 6, noticePeriodDays: 14);

        Assert.Equal(6, country.ProbationMonths);
        Assert.Equal(14, country.NoticePeriodDays);
    }

    [Theory]
    [InlineData(-1, 30, "probationMonths")]
    [InlineData(25, 30, "probationMonths")]
    [InlineData(3, -1, "noticePeriodDays")]
    [InlineData(3, 366, "noticePeriodDays")]
    public void Out_of_range_values_are_rejected(int probation, int notice, string field)
    {
        var country = new CountrySettings("PK", "Pakistan", "PKR");

        var ex = Assert.Throws<DomainValidationException>(() => country.UpdateEmploymentDefaults(probation, notice));

        Assert.Equal(field, ex.Field);
    }
}
