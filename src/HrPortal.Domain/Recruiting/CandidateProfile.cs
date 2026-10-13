using System.Globalization;
using HrPortal.Domain.Common;
using HrPortal.Domain.Files;

namespace HrPortal.Domain.Recruiting;

/// <summary>
/// CT-02: what we know about a person as a candidate. One per <see cref="Employees.Person"/>, shared by every
/// application and the Resume Bank, so there is no double entry. Salary figures are encrypted (NFR-03) and kept in
/// the currency they were given in (MC-02).
/// </summary>
public sealed class CandidateProfile
{
    private CandidateProfile()
    {
    }

    public CandidateProfile(Guid personId, DateTimeOffset createdAt)
    {
        PersonId = personId;
        CreatedAtUtc = createdAt.ToUniversalTime();
    }

    public Guid PersonId { get; private set; }

    public string? CurrentEmployer { get; private set; }

    public int? NoticePeriodDays { get; private set; }

    [Sensitive]
    public string? CurrentCtcAmount { get; private set; }

    public string? CurrentCtcCurrency { get; private set; }

    [Sensitive]
    public string? ExpectedCtcAmount { get; private set; }

    public string? ExpectedCtcCurrency { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Money? CurrentCtc => ToMoney(CurrentCtcAmount, CurrentCtcCurrency);

    public Money? ExpectedCtc => ToMoney(ExpectedCtcAmount, ExpectedCtcCurrency);

    /// <param name="allowedCurrencies">The countries' currencies (PKR, USD); there is no conversion between them.</param>
    public void Update(string? currentEmployer, int? noticePeriodDays, Money? currentCtc, Money? expectedCtc, IReadOnlySet<string> allowedCurrencies)
    {
        CurrentEmployer = Guard.OptionalText("currentEmployer", currentEmployer, 200);
        NoticePeriodDays = noticePeriodDays is { } days
            ? Guard.Range("noticePeriodDays", days, 0, 365, "Notice period must be between 0 and 365 days.")
            : null;
        (CurrentCtcAmount, CurrentCtcCurrency) = Store("currentCtc", currentCtc, allowedCurrencies);
        (ExpectedCtcAmount, ExpectedCtcCurrency) = Store("expectedCtc", expectedCtc, allowedCurrencies);
    }

    private static (string?, string?) Store(string field, Money? amount, IReadOnlySet<string> allowedCurrencies)
    {
        if (amount is not { } money)
        {
            return (null, null);
        }

        if (!allowedCurrencies.Contains(money.Currency))
        {
            throw new DomainValidationException(field, $"Use one of: {string.Join(", ", allowedCurrencies.Order())}.");
        }

        if (money.Amount <= 0 || money.Amount >= 1_000_000_000_000m || decimal.Round(money.Amount, 2) != money.Amount)
        {
            throw new DomainValidationException(field, "Enter a positive amount with at most two decimal places.");
        }

        return (money.Amount.ToString(CultureInfo.InvariantCulture), money.Currency);
    }

    private static Money? ToMoney(string? amount, string? currency) =>
        amount is null || currency is null ? null : new Money(decimal.Parse(amount, CultureInfo.InvariantCulture), currency);
}

/// <summary>A CV (RB-01) stored via <see cref="IFileStore"/>, only ever served through the API.</summary>
public sealed class CandidateCv : Entity
{
    public const string Container = "candidate-cvs";

    private CandidateCv()
    {
    }

    public CandidateCv(Guid personId, UploadedFile file, string storageKey, string uploadedBy, DateTimeOffset uploadedAt)
    {
        PersonId = personId;
        FileName = file.FileName;
        ContentType = file.ContentType;
        SizeBytes = file.SizeBytes;
        StorageKey = storageKey;
        UploadedBy = uploadedBy;
        UploadedAtUtc = uploadedAt.ToUniversalTime();
    }

    public Guid PersonId { get; private set; }

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public string StorageKey { get; private set; } = null!;

    public string UploadedBy { get; private set; } = null!;

    public DateTimeOffset UploadedAtUtc { get; private set; }
}

/// <summary>CT-03: the configurable list rejection reasons are picked from. Managed by HR Admins.</summary>
public sealed class RejectionReason : Entity
{
    private RejectionReason()
    {
    }

    public RejectionReason(string name, int sortOrder)
    {
        Update(name, sortOrder, isActive: true);
    }

    /// <summary>For seeding with fixed ids.</summary>
    public RejectionReason(Guid id, string name, int sortOrder)
        : this(name, sortOrder)
    {
        Id = id;
    }

    public string Name { get; private set; } = null!;

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, int sortOrder, bool isActive)
    {
        Name = Guard.RequiredText(nameof(name), name, 100);
        SortOrder = Guard.Range(nameof(sortOrder), sortOrder, 0, 1000, "Sort order must be between 0 and 1000.");
        IsActive = isActive;
    }
}
