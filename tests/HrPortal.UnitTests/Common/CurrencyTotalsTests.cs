using HrPortal.Domain.Common;

namespace HrPortal.UnitTests.Common;

public class CurrencyTotalsTests
{
    [Fact]
    public void MC_02_totals_across_countries_stay_separate_per_currency()
    {
        var totals = CurrencyTotals.Of([Money.Pkr(2_400_000), Money.Usd(90_000), Money.Pkr(1_600_000), Money.Usd(10_000.50m)]);

        Assert.Equal([Money.Pkr(4_000_000), Money.Usd(100_000.50m)], totals.Totals);
        Assert.Equal(Money.Usd(100_000.50m), totals.For("USD"));
        Assert.Null(totals.For("EUR"));
    }

    [Fact]
    public void MC_02_money_in_different_currencies_is_never_added_together()
    {
        Assert.Throws<InvalidOperationException>(() => Money.Pkr(1).Add(Money.Usd(1)));
    }
}
