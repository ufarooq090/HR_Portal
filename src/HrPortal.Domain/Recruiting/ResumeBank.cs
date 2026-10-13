using System.Text.Json.Serialization;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;

namespace HrPortal.Domain.Recruiting;

/// <summary>RB-02. Blacklisted is set only by HR Admins and is never overwritten automatically.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResumeBankStatus>))]
public enum ResumeBankStatus
{
    Shortlisted,
    NotShortlisted,
    NoShow,
    OfferDeclined,
    Resigned,
    Terminated,

    /// <summary>Left without notice; kept apart from Terminated so rehiring decisions can tell them apart.</summary>
    Absconded,
    Hired,
    Blacklisted,
}

[JsonConverter(typeof(JsonStringEnumConverter<StatusChangeSource>))]
public enum StatusChangeSource
{
    Manual,
    Pipeline,
    EmployeeExit,
    Import,
}

/// <summary>RB-03: how the status follows the Candidate Tracker and employee exits.</summary>
public static class ResumeBankStatusRules
{
    /// <summary>The status a pipeline stage implies, or null when it says nothing new (Applied, Withdrawn, On hold).</summary>
    public static ResumeBankStatus? FromPipeline(PipelineStage stage) => stage switch
    {
        PipelineStage.Screening or PipelineStage.Interview or PipelineStage.Offer or PipelineStage.OfferAccepted => ResumeBankStatus.Shortlisted,
        PipelineStage.Rejected => ResumeBankStatus.NotShortlisted,
        PipelineStage.NoShow => ResumeBankStatus.NoShow,
        PipelineStage.OfferDeclined => ResumeBankStatus.OfferDeclined,
        PipelineStage.Joined => ResumeBankStatus.Hired,
        _ => null,
    };

    public static ResumeBankStatus? FromEmployeeExit(EmploymentStatus status) => status switch
    {
        EmploymentStatus.Resigned => ResumeBankStatus.Resigned,
        EmploymentStatus.Terminated => ResumeBankStatus.Terminated,
        EmploymentStatus.Absconded => ResumeBankStatus.Absconded,
        _ => null,
    };
}

/// <summary>RB-02/03: one change of a candidate's Resume Bank status, for the timeline and audit.</summary>
public sealed class ResumeBankStatusChange : Entity
{
    private ResumeBankStatusChange()
    {
    }

    internal ResumeBankStatusChange(Guid personId, ResumeBankStatus? from, ResumeBankStatus? to, StatusChangeSource source, string? reason, Actor actor, DateTimeOffset at)
    {
        PersonId = personId;
        FromStatus = from;
        ToStatus = to;
        Source = source;
        Reason = reason;
        ActorId = actor.UserId;
        ActorName = actor.Name;
        AtUtc = at.ToUniversalTime();
    }

    public Guid PersonId { get; private set; }

    public ResumeBankStatus? FromStatus { get; private set; }

    public ResumeBankStatus? ToStatus { get; private set; }

    public StatusChangeSource Source { get; private set; }

    public string? Reason { get; private set; }

    public string ActorId { get; private set; } = null!;

    public string ActorName { get; private set; } = null!;

    public DateTimeOffset AtUtc { get; private set; }
}

/// <summary>RB-06: a note on the candidate (not on one application). HR only.</summary>
public sealed class CandidateNote : Entity
{
    private CandidateNote()
    {
    }

    public CandidateNote(Guid personId, string body, Actor author, DateTimeOffset at)
    {
        PersonId = personId;
        Body = Guard.RequiredText(nameof(body), body, 4000);
        AuthorId = author.UserId;
        AuthorName = author.Name;
        CreatedAtUtc = at.ToUniversalTime();
    }

    public Guid PersonId { get; private set; }

    public string Body { get; private set; } = null!;

    public string AuthorId { get; private set; } = null!;

    public string AuthorName { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }
}

/// <summary>What one user may see and do with one Resume Bank candidate.</summary>
public sealed record ResumeBankPermissions(
    bool CanView,
    bool CanEdit,
    bool CanSeeContact,
    bool CanSeeCompensation,
    bool CanSeeNotes,
    bool CanBlacklist);

/// <summary>
/// Resume Bank access (RB, permission matrix "Resume Bank"): HR see and manage candidates in their countries
/// (MC-03). Hiring managers search and view the CVs, skills and past outcomes of candidates in their own country
/// (MC-03), but not contact details, salary or notes. A candidate on one of their own requisitions is visible to them
/// whatever the country, with salary, as in the Candidate Tracker. Only HR Admins blacklist.
/// </summary>
public static class ResumeBankRules
{
    public static bool IsHrFor(UserAccess access, string countryCode, Capability capability) =>
        access.ScopesFor(HrModule.ResumeBank, capability).Contains(DataScope.Countries) && access.HrCountries.Includes(countryCode);

    /// <param name="hiringManagerOfAnApplication">The user is hiring manager on one of the candidate's requisitions.</param>
    public static ResumeBankPermissions For(UserAccess access, string countryCode, bool hiringManagerOfAnApplication)
    {
        var hr = IsHrFor(access, countryCode, Capability.View);
        var scopes = access.ScopesFor(HrModule.ResumeBank, Capability.View);
        var inScope = scopes.Contains(DataScope.All)
            || (scopes.Contains(DataScope.HomeCountry) && string.Equals(access.HomeCountryCode, countryCode, StringComparison.OrdinalIgnoreCase));
        return new ResumeBankPermissions(
            CanView: hr || inScope || hiringManagerOfAnApplication,
            CanEdit: IsHrFor(access, countryCode, Capability.Edit),
            CanSeeContact: hr,
            CanSeeCompensation: hr || hiringManagerOfAnApplication,
            CanSeeNotes: hr,
            CanBlacklist: IsHrFor(access, countryCode, Capability.Edit) && access.Roles.Contains(Roles.HrAdmin));
    }
}
