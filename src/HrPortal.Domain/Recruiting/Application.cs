using System.Text.Json.Serialization;
using HrPortal.Domain.Common;
using HrPortal.Domain.Requisitions;

namespace HrPortal.Domain.Recruiting;

[JsonConverter(typeof(JsonStringEnumConverter<CandidateSource>))]
public enum CandidateSource
{
    Portal,
    Referral,
    Agency,
    JobBoard,
    WalkIn,
}

/// <summary>
/// Pipeline stages. Applied → Screening → Interview (rounds 1..n) → Offer → Offer accepted → Joined are the active
/// path; the rest are exit statuses, reachable from any stage.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PipelineStage>))]
public enum PipelineStage
{
    Applied,
    Screening,
    Interview,
    Offer,
    OfferAccepted,
    Joined,
    Rejected,
    Withdrawn,
    NoShow,
    OnHold,
    OfferDeclined,
}

public static class PipelineStages
{
    public static readonly IReadOnlyList<PipelineStage> Active =
        [PipelineStage.Applied, PipelineStage.Screening, PipelineStage.Interview, PipelineStage.Offer, PipelineStage.OfferAccepted, PipelineStage.Joined];

    public static readonly IReadOnlyList<PipelineStage> Exits =
        [PipelineStage.Rejected, PipelineStage.Withdrawn, PipelineStage.NoShow, PipelineStage.OnHold, PipelineStage.OfferDeclined];

    public static bool IsExit(this PipelineStage stage) => Exits.Contains(stage);
}

/// <summary>
/// One person applying to one requisition (the core data model's Application). Every stage change is logged with
/// who, when and why (CT-03); a rejection needs a reason from the configurable list.
/// </summary>
public sealed class Application : Entity
{
    private readonly List<ApplicationStageChange> _history = [];

    private Application()
    {
    }

    public Guid PersonId { get; private set; }

    public Guid RequisitionId { get; private set; }

    public CandidateSource Source { get; private set; }

    public PipelineStage Stage { get; private set; }

    /// <summary>Set while <see cref="Stage"/> is <see cref="PipelineStage.Interview"/>.</summary>
    public int? InterviewRound { get; private set; }

    public DateTimeOffset AppliedAtUtc { get; private set; }

    public DateTimeOffset StageChangedAtUtc { get; private set; }

    public IReadOnlyList<ApplicationStageChange> History => _history;

    /// <summary>REQ-03: candidates are tracked against open positions only.</summary>
    public static Application Create(Guid personId, Requisition requisition, CandidateSource source, Actor actor, DateTimeOffset now)
    {
        if (!requisition.AcceptsCandidates)
        {
            throw new DomainValidationException("requisitionId", $"Candidates can only be added to open requisitions; this one is {Requisition.Label(requisition.Status)}.");
        }

        var application = new Application
        {
            PersonId = personId,
            RequisitionId = requisition.Id,
            Source = source,
            Stage = PipelineStage.Applied,
            AppliedAtUtc = now.ToUniversalTime(),
            StageChangedAtUtc = now.ToUniversalTime(),
        };
        application._history.Add(new ApplicationStageChange(application.Id, null, null, PipelineStage.Applied, null, null, null, actor, now));
        return application;
    }

    /// <summary>CT-01/03: move to another stage (drag-and-drop on the board or the list), logged.</summary>
    /// <param name="interviewRounds">The requisition's number of rounds.</param>
    public void MoveTo(
        PipelineStage stage, int? round, RejectionReason? rejectionReason, string? reason, int interviewRounds, Actor actor, DateTimeOffset now)
    {
        if (Stage == PipelineStage.Joined)
        {
            throw new DomainValidationException("stage", "The candidate has joined; their application is closed.");
        }

        if (stage == PipelineStage.Interview)
        {
            if (round is not { } r || r < 1 || r > interviewRounds)
            {
                throw new DomainValidationException("round", $"Choose interview round 1 to {interviewRounds}.");
            }
        }
        else if (round is not null)
        {
            throw new DomainValidationException("round", "Only the interview stage has rounds.");
        }

        if (stage == Stage && round == InterviewRound)
        {
            throw new DomainValidationException("stage", "The candidate is already at this stage.");
        }

        var allowedFrom = stage switch
        {
            PipelineStage.Joined => [PipelineStage.OfferAccepted],
            PipelineStage.OfferAccepted => [PipelineStage.Offer],
            PipelineStage.OfferDeclined => new[] { PipelineStage.Offer, PipelineStage.OfferAccepted },
            _ => null,
        };
        if (allowedFrom is not null && !allowedFrom.Contains(Stage))
        {
            throw new DomainValidationException("stage", $"{Label(stage)} can only follow {string.Join(" or ", allowedFrom.Select(Label))}.");
        }

        if (stage == PipelineStage.Rejected)
        {
            if (rejectionReason is not { IsActive: true })
            {
                throw new DomainValidationException("rejectionReasonId", "Choose a rejection reason from the list.");
            }
        }
        else if (rejectionReason is not null)
        {
            throw new DomainValidationException("rejectionReasonId", "A rejection reason applies only when rejecting.");
        }

        _history.Add(new ApplicationStageChange(Id, Stage, InterviewRound, stage, round, rejectionReason, Guard.OptionalText("reason", reason, 1000), actor, now));
        Stage = stage;
        InterviewRound = round;
        StageChangedAtUtc = now.ToUniversalTime();
    }

    public static string Label(PipelineStage stage) => stage switch
    {
        PipelineStage.OfferAccepted => "Offer accepted",
        PipelineStage.OfferDeclined => "Offer declined",
        PipelineStage.NoShow => "No-show",
        PipelineStage.OnHold => "On hold",
        _ => stage.ToString(),
    };
}

/// <summary>CT-03: one logged stage change.</summary>
public sealed class ApplicationStageChange : Entity
{
    private ApplicationStageChange()
    {
    }

    internal ApplicationStageChange(
        Guid applicationId,
        PipelineStage? fromStage,
        int? fromRound,
        PipelineStage toStage,
        int? toRound,
        RejectionReason? rejectionReason,
        string? reason,
        Actor actor,
        DateTimeOffset at)
    {
        ApplicationId = applicationId;
        FromStage = fromStage;
        FromRound = fromRound;
        ToStage = toStage;
        ToRound = toRound;
        RejectionReasonId = rejectionReason?.Id;
        RejectionReasonName = rejectionReason?.Name;
        Reason = reason;
        ActorId = actor.UserId;
        ActorName = actor.Name;
        AtUtc = at.ToUniversalTime();
    }

    public Guid ApplicationId { get; private set; }

    public PipelineStage? FromStage { get; private set; }

    public int? FromRound { get; private set; }

    public PipelineStage ToStage { get; private set; }

    public int? ToRound { get; private set; }

    public Guid? RejectionReasonId { get; private set; }

    /// <summary>Kept as text so renaming a reason later does not rewrite history.</summary>
    public string? RejectionReasonName { get; private set; }

    public string? Reason { get; private set; }

    public string ActorId { get; private set; } = null!;

    public string ActorName { get; private set; } = null!;

    public DateTimeOffset AtUtc { get; private set; }
}

/// <summary>CT-08: a free-text note with author and time. Not shown to interviewers, to keep their feedback independent.</summary>
public sealed class ApplicationNote : Entity
{
    private ApplicationNote()
    {
    }

    public ApplicationNote(Guid applicationId, string body, Actor author, DateTimeOffset at)
    {
        ApplicationId = applicationId;
        Body = Guard.RequiredText(nameof(body), body, 4000);
        AuthorId = author.UserId;
        AuthorName = author.Name;
        CreatedAtUtc = at.ToUniversalTime();
    }

    public Guid ApplicationId { get; private set; }

    public string Body { get; private set; } = null!;

    public string AuthorId { get; private set; } = null!;

    public string AuthorName { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
