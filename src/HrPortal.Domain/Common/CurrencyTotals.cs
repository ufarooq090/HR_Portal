namespace HrPortal.Domain.Common;

/// <summary>
/// Totals kept per currency (MC-02, HR decision 13 Oct 2026). Amounts in different currencies are never added
/// together or converted: a report covering Pakistan and the US shows a PKR figure and a USD figure.
/// </summary>
public sealed class CurrencyTotals
{
    private readonly SortedDictionary<string, Money> _totals = new(StringComparer.Ordinal);

    /// <summary>One total per currency, ordered by currency code.</summary>
    public IReadOnlyList<Money> Totals => _totals.Values.ToList();

    public CurrencyTotals Add(Money amount)
    {
        _totals[amount.Currency] = _totals.TryGetValue(amount.Currency, out var total) ? total.Add(amount) : amount;
        return this;
    }

    public static CurrencyTotals Of(IEnumerable<Money> amounts) =>
        amounts.Aggregate(new CurrencyTotals(), (totals, amount) => totals.Add(amount));

    public Money? For(string currency) => _totals.TryGetValue(currency, out var total) ? total : null;
}
