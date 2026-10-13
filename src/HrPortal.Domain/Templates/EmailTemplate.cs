using System.Text.Json.Serialization;
using HrPortal.Domain.Common;
using HrPortal.Domain.Requisitions;

namespace HrPortal.Domain.Templates;

/// <summary>ET-06 categories. Termination is restricted: HR Admins only, never in bulk (rule 8).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TemplateCategory>))]
public enum TemplateCategory
{
    Interview,
    Rejection,
    Offer,
    Onboarding,
    Probation,
    Separation,
    Termination,
    Requisition,
    Performance,
    General,
}

/// <summary>The record an email is sent from; it decides the recipient and which merge fields have values.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TemplateRecordType>))]
public enum TemplateRecordType
{
    /// <summary>A candidate's application to a requisition; sent to the candidate.</summary>
    Application,

    /// <summary>An employee; sent to their work email.</summary>
    Employee,

    /// <summary>A requisition awaiting approval; sent to the current approver.</summary>
    Requisition,
}

/// <summary>OL-02: the mailbox an email goes from. Stored now; Outlook sending comes in Phase 2.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SenderMailbox>))]
public enum SenderMailbox
{
    /// <summary>The shared HR mailbox (e.g. hr@company.com).</summary>
    HrMailbox,

    /// <summary>The logged-in user's own mailbox.</summary>
    CurrentUser,
}

/// <summary>The editable content of a template.</summary>
public sealed record TemplateContent(
    string Name,
    TemplateCategory Category,
    TemplateRecordType RecordType,
    string Subject,
    string BodyHtml,
    string? CountryCode,
    SenderMailbox DefaultSender,
    bool CcManager,
    bool IsActive);

/// <summary>
/// ET-01/02/06: an email template HR Admins edit without developer help. Every change creates a new, immutable
/// <see cref="EmailTemplateVersion"/>; emails record the version they were sent from. The body is HTML that the
/// caller has already sanitised. Merge fields are written as {{field_name}} (see <see cref="MergeFields"/>).
/// </summary>
public sealed class EmailTemplate : Entity
{
    public const int MaxBodyLength = 100_000;
    public const int MaxSubjectLength = 300;

    private readonly List<EmailTemplateVersion> _versions = [];

    private EmailTemplate()
    {
    }

    public string Name { get; private set; } = null!;

    public TemplateCategory Category { get; private set; }

    public TemplateRecordType RecordType { get; private set; }

    public string Subject { get; private set; } = null!;

    public string BodyHtml { get; private set; } = null!;

    /// <summary>PK or US, or null for both countries.</summary>
    public string? CountryCode { get; private set; }

    public SenderMailbox DefaultSender { get; private set; }

    /// <summary>Copy the employee's manager (probation, PIP notices).</summary>
    public bool CcManager { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>For starter templates: what will send them automatically in Phase 2 (ET-07). Information only.</summary>
    public string? Trigger { get; private set; }

    /// <summary>Identifies a starter template from the spec, so seeding never adds it twice.</summary>
    public string? StarterKey { get; private set; }

    public int Version { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<EmailTemplateVersion> Versions => _versions;

    /// <summary>Termination communication: HR Admins only, one employee at a time, never automatic.</summary>
    public bool IsRestricted => Category == TemplateCategory.Termination;

    public static EmailTemplate Create(TemplateContent content, Actor actor, DateTimeOffset now, string? trigger = null, string? starterKey = null)
    {
        var template = new EmailTemplate { Trigger = trigger, StarterKey = starterKey };
        template.Apply(content, actor, now);
        return template;
    }

    /// <summary>Saves a new version; returns false (and changes nothing) when the content is unchanged.</summary>
    public bool Update(TemplateContent content, Actor actor, DateTimeOffset now)
    {
        if (content == Current)
        {
            return false;
        }

        Apply(content, actor, now);
        return true;
    }

    public TemplateContent Current => new(Name, Category, RecordType, Subject, BodyHtml, CountryCode, DefaultSender, CcManager, IsActive);

    /// <summary>Whether the template may be used for a record in this country.</summary>
    public bool AppliesTo(string countryCode) => CountryCode is null || string.Equals(CountryCode, countryCode, StringComparison.OrdinalIgnoreCase);

    private void Apply(TemplateContent content, Actor actor, DateTimeOffset now)
    {
        if (content.RecordType != RecordType && Version > 0)
        {
            throw new DomainValidationException("recordType", "The record type cannot change once a template exists; create a new template instead.");
        }

        if (content.Category == TemplateCategory.Termination && content.RecordType != TemplateRecordType.Employee)
        {
            throw new DomainValidationException("recordType", "Termination communication is sent to employees.");
        }

        if (content.CcManager && content.RecordType != TemplateRecordType.Employee)
        {
            throw new DomainValidationException("ccManager", "Only emails to employees can copy their manager.");
        }

        Name = Guard.RequiredText("name", content.Name, 150);
        Category = content.Category;
        RecordType = content.RecordType;
        Subject = Guard.RequiredText("subject", content.Subject, MaxSubjectLength);
        BodyHtml = Guard.RequiredText("bodyHtml", content.BodyHtml, MaxBodyLength);
        CountryCode = content.CountryCode is null ? null : Guard.CountryCode("countryCode", content.CountryCode);
        DefaultSender = content.DefaultSender;
        CcManager = content.CcManager;
        IsActive = content.IsActive;

        var unknown = MergeFields.UnknownIn(Subject + " " + BodyHtml, RecordType);
        if (unknown.Count > 0)
        {
            throw new DomainValidationException(
                "bodyHtml", $"These merge fields are not available for {RecordType.ToString().ToLowerInvariant()} emails: {string.Join(", ", unknown.Select(f => $"{{{{{f}}}}}"))}.");
        }

        Version++;
        UpdatedAtUtc = now.ToUniversalTime();
        _versions.Add(new EmailTemplateVersion(Id, Version, Current, actor, now));
    }
}

/// <summary>ET-06: one saved state of a template. Never changed or deleted.</summary>
public sealed class EmailTemplateVersion : Entity
{
    private EmailTemplateVersion()
    {
    }

    internal EmailTemplateVersion(Guid templateId, int version, TemplateContent content, Actor actor, DateTimeOffset at)
    {
        TemplateId = templateId;
        Version = version;
        Name = content.Name;
        Category = content.Category;
        Subject = content.Subject;
        BodyHtml = content.BodyHtml;
        CountryCode = content.CountryCode;
        DefaultSender = content.DefaultSender;
        CcManager = content.CcManager;
        IsActive = content.IsActive;
        ChangedById = actor.UserId;
        ChangedByName = actor.Name;
        AtUtc = at.ToUniversalTime();
    }

    public Guid TemplateId { get; private set; }

    public int Version { get; private set; }

    public string Name { get; private set; } = null!;

    public TemplateCategory Category { get; private set; }

    public string Subject { get; private set; } = null!;

    public string BodyHtml { get; private set; } = null!;

    public string? CountryCode { get; private set; }

    public SenderMailbox DefaultSender { get; private set; }

    public bool CcManager { get; private set; }

    public bool IsActive { get; private set; }

    public string ChangedById { get; private set; } = null!;

    public string ChangedByName { get; private set; } = null!;

    public DateTimeOffset AtUtc { get; private set; }
}
