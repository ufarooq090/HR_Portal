namespace HrPortal.Domain.Common;

/// <summary>An amount of money in a specific ISO 4217 currency. Never use a bare number for money.</summary>
public readonly record struct Money
{
    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException("Currency must be a three-letter upper-case ISO 4217 code.", nameof(currency));
        }

        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Pkr(decimal amount) => new(amount, "PKR");

    public static Money Usd(decimal amount) => new(amount, "USD");

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public override string ToString() => $"{Currency} {Amount:N2}";

    private void EnsureSameCurrency(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException($"Cannot combine {Currency} with {other.Currency}.");
        }
    }
}
