using HrPortal.Domain.Common;

namespace HrPortal.UnitTests.Common;

public class MoneyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("pkr")]
    [InlineData("US")]
    [InlineData("USDX")]
    public void Rejects_invalid_currency_codes(string currency)
    {
        Assert.Throws<ArgumentException>(() => new Money(10m, currency));
    }

    [Fact]
    public void Adds_amounts_in_the_same_currency()
    {
        Assert.Equal(Money.Pkr(150_000m), Money.Pkr(100_000m).Add(Money.Pkr(50_000m)));
    }

    [Fact]
    public void Refuses_to_mix_currencies()
    {
        Assert.Throws<InvalidOperationException>(() => Money.Pkr(1m).Add(Money.Usd(1m)));
    }
}
