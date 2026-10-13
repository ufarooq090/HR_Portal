using System.Text.Json.Serialization;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;

namespace HrPortal.Domain.Recruiting;

[JsonConverter(typeof(JsonStringEnumConverter<InterviewMode>))]
public enum InterviewMode
{
    InPerson,
    Teams,
    Phone,
}

[JsonConverter(typeof(JsonStringEnumConverter<InterviewStatus>))]
public enum InterviewStatus
{
    Scheduled,
    Cancelled,
}

[JsonConverter(typeof(JsonStringEnumConverter<Recommendation>))]
public enum Recommendation
{
    StrongHire,
    Hire,
    NoHire,
}

/// <summary>
/// An interview round, recorded by HR (CT-04 Outlook invites come in Phase 2; for now it goes through
/// <see cref="Notifications.ICalendarService"/>, which only logs locally). Being on the panel is what lets an
/// interviewer see the candidate. The requisition's scorecard criteria are copied on when scheduled.
/// </summary>
public sealed class Interview : Entity
{
    public const int MaxPanel = 6;

    private readonly List<InterviewPanelist> _panel = [];

    private Interview()
    {
    }

    public Guid ApplicationId { get; private set; }

    public int Round { get; private set; }

    public DateTimeOffset StartUtc { get; private set; }

    public int DurationMinutes { get; private set; }

    public InterviewMode Mode { get; private set; }

    /// <summary>Room, address, meeting link or phone number.</summary>
    public string? Location { get; private set; }

    public InterviewStatus Status { get; private set; }

    public List<string> Criteria { get; private set; } = [];

    /// <summary>The calendar provider's event id, for cancelling.</summary>
    public string? CalendarEventId { get; private set; }

    public string? OrganizerEmail { get; private set; }

    public IReadOnlyList<InterviewPanelist> Panel => _panel;

    public static Interview Schedule(
        Application application,
        int round,
        int interviewRounds,
        DateTimeOffset start,
        int durationMinutes,
        InterviewMode mode,
        string? location,
        IReadOnlyList<Employee> panel,
        IReadOnlyList<string> criteria)
    {
        if (application.Stage.IsExit() || application.Stage == PipelineStage.Joined)
        {
            throw new DomainValidationException("applicationId", $"The candidate is {Application.Label(application.Stage).ToLowerInvariant()}; interviews cannot be added.");
        }

        Guard.Range(nameof(round), round, 1, interviewRounds, $"Choose round 1 to {interviewRounds}.");
        Guard.Range(nameof(durationMinutes), durationMinutes, 15, 480, "Interviews last between 15 minutes and 8 hours.");
        if (panel.Count is 0 or > MaxPanel || panel.Select(p => p.Id).Distinct().Count() != panel.Count)
        {
            throw new DomainValidationException("panelEmployeeIds", $"Choose 1 to {MaxPanel} different interviewers.");
        }

        if (panel.Any(p => !p.Status.IsActive()))
        {
            throw new DomainValidationException("panelEmployeeIds", "Interviewers must be current employees.");
        }

        var interview = new Interview
        {
            ApplicationId = application.Id,
            Round = round,
            StartUtc = start.ToUniversalTime(),
            DurationMinutes = durationMinutes,
            Mode = mode,
            Location = Guard.OptionalText(nameof(location), location, 500),
            Status = InterviewStatus.Scheduled,
            Criteria = [.. criteria],
        };
        interview._panel.AddRange(panel.Select(p => new InterviewPanelist(interview.Id, p.Id)));
        return interview;
    }

    public void RecordCalendarEvent(string organizerEmail, string eventId)
    {
        OrganizerEmail = organizerEmail;
        CalendarEventId = eventId;
    }

    public void Cancel()
    {
        if (Status == InterviewStatus.Cancelled)
        {
            throw new DomainValidationException("status", "The interview is already cancelled.");
        }

        Status = InterviewStatus.Cancelled;
    }

    public bool HasPanelist(Guid employeeId) => _panel.Any(p => p.EmployeeId == employeeId);
}

public sealed class InterviewPanelist
{
    private InterviewPanelist()
    {
    }

    internal InterviewPanelist(Guid interviewId, Guid employeeId)
    {
        InterviewId = interviewId;
        EmployeeId = employeeId;
    }

    public Guid InterviewId { get; private set; }

    public Guid EmployeeId { get; private set; }
}

public sealed record CriterionRating(string Criterion, int Rating, string? Comment);

/// <summary>
/// CT-06: one panelist's structured feedback on one interview. Submitted once and final, so nobody can adjust
/// theirs after reading others' (CT-07).
/// </summary>
public sealed class Scorecard : Entity
{
    private Scorecard()
    {
    }

    public Guid InterviewId { get; private set; }

    public Guid InterviewerEmployeeId { get; private set; }

    public List<CriterionRating> Ratings { get; private set; } = [];

    public Recommendation Recommendation { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset SubmittedAtUtc { get; private set; }

    public static Scorecard Submit(
        Interview interview, Guid interviewerEmployeeId, IReadOnlyList<CriterionRating> ratings, Recommendation recommendation, string? comment, DateTimeOffset now)
    {
        if (!interview.HasPanelist(interviewerEmployeeId))
        {
            throw new InvalidOperationException("Not on the panel.");
        }

        if (interview.Status == InterviewStatus.Cancelled)
        {
            throw new DomainValidationException("interviewId", "The interview was cancelled.");
        }

        var byCriterion = ratings.GroupBy(r => r.Criterion.Trim(), StringComparer.OrdinalIgnoreCase).ToList();
        if (byCriterion.Any(g => g.Count() > 1)
            || interview.Criteria.Count != byCriterion.Count
            || interview.Criteria.Any(c => !byCriterion.Any(g => string.Equals(g.Key, c, StringComparison.OrdinalIgnoreCase))))
        {
            throw new DomainValidationException("ratings", $"Rate each criterion once: {string.Join(", ", interview.Criteria)}.");
        }

        if (ratings.Any(r => r.Rating is < 1 or > 5))
        {
            throw new DomainValidationException("ratings", "Ratings go from 1 to 5.");
        }

        return new Scorecard
        {
            InterviewId = interview.Id,
            InterviewerEmployeeId = interviewerEmployeeId,
            Ratings = interview.Criteria
                .Select(c => ratings.First(r => string.Equals(r.Criterion.Trim(), c, StringComparison.OrdinalIgnoreCase)))
                .Select(r => r with { Criterion = interview.Criteria.First(c => string.Equals(c, r.Criterion.Trim(), StringComparison.OrdinalIgnoreCase)), Comment = Guard.OptionalText("ratings", r.Comment, 1000) })
                .ToList(),
            Recommendation = recommendation,
            Comment = Guard.OptionalText(nameof(comment), comment, 4000),
            SubmittedAtUtc = now.ToUniversalTime(),
        };
    }
}
