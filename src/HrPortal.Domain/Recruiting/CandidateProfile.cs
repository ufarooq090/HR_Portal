using System.Globalization;
using HrPortal.Domain.Common;
using HrPortal.Domain.Files;
using HrPortal.Domain.Requisitions;

namespace HrPortal.Domain.Recruiting;

/// <summary>
/// CT-02 and the Resume Bank: what we know about a person as a candidate. One per <see cref="Employees.Person"/>,
/// shared by every application and the Resume Bank, so there is no double entry. Salary figures are encrypted
/// (NFR-03) and kept in the currency they were given in (MC-02). The country decides which HR users see the
/// candidate (MC-03).
/// </summary>
public sealed class CandidateProfile
{
    public const int MaxSkills = 50;

    private readonly List<ResumeBankStatusChange> _statusHistory = [];

    private CandidateProfile()
    {
    }

    public CandidateProfile(Guid personId, string countryCode, DateTimeOffset createdAt)
    {
        PersonId = personId;
        CountryCode = Guard.CountryCode(nameof(countryCode), countryCode);
        CreatedAtUtc = createdAt.ToUniversalTime();
    }

    public Guid PersonId { get; private set; }

    /// <summary>Where the candidate is based; HR in this country manage them.</summary>
    public string CountryCode { get; private set; } = null!;

    /// <summary>City or area, for the location filter (RB-04).</summary>
    public string? Location { get; private set; }

    public string? CurrentEmployer { get; private set; }

    public int? NoticePeriodDays { get; private set; }

    public decimal? ExperienceYears { get; private set; }

    /// <summary>Lower-case, so searches match regardless of case.</summary>
    public List<string> Skills { get; private set; } = [];

    /// <summary>RB-06, lower-case, e.g. "good for future data roles".</summary>
    public List<string> Tags { get; private set; } = [];

    [Sensitive]
    public string? CurrentCtcAmount { get; private set; }

    public string? CurrentCtcCurrency { get; private set; }

    [Sensitive]
    public string? ExpectedCtcAmount { get; private set; }

    public string? ExpectedCtcCurrency { get; private set; }

    public ResumeBankStatus? Status { get; private set; }

    /// <summary>Why HR blacklisted the candidate; kept while blacklisted.</summary>
    public string? BlacklistReason { get; private set; }

    public DateTimeOffset? LastContactedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<ResumeBankStatusChange> StatusHistory => _statusHistory;

    public Money? CurrentCtc => ToMoney(CurrentCtcAmount, CurrentCtcCurrency);

    public Money? ExpectedCtc => ToMoney(ExpectedCtcAmount, ExpectedCtcCurrency);

    public bool IsBlacklisted => Status == ResumeBankStatus.Blacklisted;

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

    /// <summary>RB-04/06: what the Resume Bank searches and filters on.</summary>
    public void UpdateResumeDetails(string countryCode, string? location, decimal? experienceYears, IEnumerable<string> skills, IEnumerable<string> tags)
    {
        if (experienceYears is { } years && (years < 0 || years > 60 || decimal.Round(years, 1) != years))
        {
            throw new DomainValidationException(nameof(experienceYears), "Experience is 0 to 60 years, to one decimal place.");
        }

        CountryCode = Guard.CountryCode(nameof(countryCode), countryCode);
        Location = Guard.OptionalText(nameof(location), location, 100);
        ExperienceYears = experienceYears;
        Skills = Normalise(nameof(skills), skills, 60);
        Tags = Normalise(nameof(tags), tags, 60);
    }

    /// <summary>RB-03: the Candidate Tracker and employee exits update the status, except a blacklisted one.</summary>
    public void ApplyAutomaticStatus(ResumeBankStatus? status, StatusChangeSource source, string reason, Actor actor, DateTimeOffset now)
    {
        if (status is null || IsBlacklisted || status == Status)
        {
            return;
        }

        Change(status, source, reason, actor, now);
    }

    /// <summary>HR sets a status by hand; blacklisting has its own rules.</summary>
    public void SetStatus(ResumeBankStatus? status, string? reason, Actor actor, DateTimeOffset now)
    {
        if (status == ResumeBankStatus.Blacklisted || IsBlacklisted)
        {
            throw new DomainValidationException(nameof(status), "Blacklisting is done separately, by an HR Admin.");
        }

        if (status == Status)
        {
            throw new DomainValidationException(nameof(status), "The candidate already has this status.");
        }

        Change(status, StatusChangeSource.Manual, Guard.OptionalText(nameof(reason), reason, 1000), actor, now);
    }

    /// <summary>HR Admins only (checked by the caller); a reason is required and kept.</summary>
    public void Blacklist(string reason, Actor actor, DateTimeOffset now)
    {
        if (IsBlacklisted)
        {
            throw new DomainValidationException("status", "The candidate is already blacklisted.");
        }

        BlacklistReason = Guard.RequiredText(nameof(reason), reason, 1000);
        Change(ResumeBankStatus.Blacklisted, StatusChangeSource.Manual, BlacklistReason, actor, now);
    }

    public void RemoveFromBlacklist(string reason, Actor actor, DateTimeOffset now)
    {
        if (!IsBlacklisted)
        {
            throw new DomainValidationException("status", "The candidate is not blacklisted.");
        }

        BlacklistReason = null;
        Change(null, StatusChangeSource.Manual, Guard.RequiredText(nameof(reason), reason, 1000), actor, now);
    }

    /// <summary>"Last contacted" (RB-04): moves forward only.</summary>
    public void MarkContacted(DateTimeOffset at)
    {
        if (LastContactedAtUtc is null || at > LastContactedAtUtc)
        {
            LastContactedAtUtc = at.ToUniversalTime();
        }
    }

    private void Change(ResumeBankStatus? status, StatusChangeSource source, string? reason, Actor actor, DateTimeOffset now)
    {
        _statusHistory.Add(new ResumeBankStatusChange(PersonId, Status, status, source, reason, actor, now));
        Status = status;
    }

    private static List<string> Normalise(string field, IEnumerable<string> values, int maxLength)
    {
        var list = values
            .Select(v => v?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (list.Count > MaxSkills || list.Any(v => v.Length > maxLength))
        {
            throw new DomainValidationException(field, $"Use at most {MaxSkills} entries of up to {maxLength} characters.");
        }

        return list;
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

    /// <summary>Longest CV text kept for search.</summary>
    public const int MaxTextLength = 200_000;

    private CandidateCv()
    {
    }

    public CandidateCv(Guid personId, UploadedFile file, string storageKey, string uploadedBy, DateTimeOffset uploadedAt, string? text)
    {
        PersonId = personId;
        FileName = file.FileName;
        ContentType = file.ContentType;
        SizeBytes = file.SizeBytes;
        StorageKey = storageKey;
        UploadedBy = uploadedBy;
        UploadedAtUtc = uploadedAt.ToUniversalTime();
        RecordText(text, uploadedAt);
    }

    public Guid PersonId { get; private set; }

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public string StorageKey { get; private set; } = null!;

    public string UploadedBy { get; private set; } = null!;

    public DateTimeOffset UploadedAtUtc { get; private set; }

    /// <summary>RB-04: the CV's text for full-text search; null for formats we cannot read (e.g. old .doc).</summary>
    [NotAudited]
    public string? ExtractedText { get; private set; }

    /// <summary>When text extraction was last attempted; null for CVs uploaded before CV search existed.</summary>
    public DateTimeOffset? TextExtractedAtUtc { get; private set; }

    /// <summary>Stores the extracted text (null when the format cannot be read) and when extraction ran.</summary>
    public void RecordText(string? text, DateTimeOffset at)
    {
        ExtractedText = text is null ? null : text.Length > MaxTextLength ? text[..MaxTextLength] : text;
        TextExtractedAtUtc = at.ToUniversalTime();
    }
}

/// <summary>Pulls plain text out of a CV for full-text search (RB-04).</summary>
public interface ICvTextExtractor
{
    /// <returns>The text, or null when the format cannot be read.</returns>
    string? Extract(Stream content, string contentType);
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
