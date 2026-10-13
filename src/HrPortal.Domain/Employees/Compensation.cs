using System.Globalization;
using System.Text.Json.Serialization;
using HrPortal.Domain.Common;
using HrPortal.Domain.Organisation;

namespace HrPortal.Domain.Employees;

[JsonConverter(typeof(JsonStringEnumConverter<CompensationReason>))]
public enum CompensationReason
{
    Joining,
    Increment,
    Promotion,
    Adjustment,
    Correction,
}

/// <summary>
/// EMP-03: annual cost to company and salary band from an effective date. Records are never edited; a change
/// adds a new record, so the list is the salary history (EMP-05). Amounts are encrypted at field level
/// (NFR-03) and stored in the employee's local currency (MC-02).
/// </summary>
public sealed class Compensation : Entity
{
    private Compensation()
    {
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    /// <summary>Invariant-culture decimal text, so it can be encrypted. Use <see cref="AnnualCtc"/>.</summary>
    [Sensitive]
    public string AnnualCtcAmount { get; private set; } = null!;

    public string CurrencyCode { get; private set; } = null!;

    [Sensitive]
    public string? SalaryBand { get; private set; }

    public CompensationReason Reason { get; private set; }

    /// <summary>Free text that may mention pay, so it is encrypted too.</summary>
    [Sensitive]
    public string? Note { get; private set; }

    public Money AnnualCtc => new(decimal.Parse(AnnualCtcAmount, CultureInfo.InvariantCulture), CurrencyCode);

    public static Compensation Create(
        Employee employee,
        CountrySettings country,
        Money annualCtc,
        string? salaryBand,
        DateOnly effectiveDate,
        CompensationReason reason,
        string? note)
    {
        if (country.CountryCode != employee.CountryCode)
        {
            throw new ArgumentException("Country does not match the employee.", nameof(country));
        }

        if (annualCtc.Currency != country.CurrencyCode)
        {
            throw new DomainValidationException(
                "currencyCode", $"Compensation for {country.Name} is recorded in {country.CurrencyCode}.");
        }

        if (annualCtc.Amount <= 0 || annualCtc.Amount >= 1_000_000_000_000m || decimal.Round(annualCtc.Amount, 2) != annualCtc.Amount)
        {
            throw new DomainValidationException("annualCtc", "Enter a positive amount with at most two decimal places.");
        }

        if (effectiveDate < employee.JoiningDate)
        {
            throw new DomainValidationException(nameof(effectiveDate), "Compensation cannot start before the joining date.");
        }

        return new Compensation
        {
            EmployeeId = employee.Id,
            EffectiveDate = effectiveDate,
            AnnualCtcAmount = annualCtc.Amount.ToString(CultureInfo.InvariantCulture),
            CurrencyCode = annualCtc.Currency,
            SalaryBand = Guard.OptionalText(nameof(salaryBand), salaryBand, 50),
            Reason = reason,
            Note = Guard.OptionalText(nameof(note), note, 500),
        };
    }
}
