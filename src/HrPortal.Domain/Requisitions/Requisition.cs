using System.Text.Json.Serialization;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;

namespace HrPortal.Domain.Requisitions;

/// <summary>REQ-04.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RequisitionStatus>))]
public enum RequisitionStatus
{
    Draft,
    PendingApproval,

    /// <summary>Approved: an open position candidates are tracked against (REQ-03).</summary>
    Open,
    OnHold,
    Filled,
    Cancelled,
}

[JsonConverter(typeof(JsonStringEnumConverter<RequisitionReason>))]
public enum RequisitionReason
{
    NewPosition,
    Replacement,
}

/// <summary>Who did something, for the status log. Taken from the signed-in user.</summary>
public sealed record Actor(string UserId, string Name, Guid? EmployeeId);

/// <summary>Editable fields of a requisition (REQ-01).</summary>
public sealed record RequisitionDetails(
    string Title,
    Department Department,
    OfficeLocation OfficeLocation,
    int Openings,
    Money BudgetMin,
    Money BudgetMax,
    RequisitionReason Reason,
    Employee? ReplacedEmployee,
    DateOnly TargetDate,
    string JobDescription,
    Employee? HiringManager = null);

/// <summary>What a decision led to, so the caller knows whom to notify.</summary>
public sealed record DecisionOutcome(bool Approved, bool Opened, RequisitionApproval? NextStep);

/// <summary>
/// A request to hire (REQ-01..04). Raised by a hiring or line manager (or HR) as a draft, submitted for approval
/// through the country's approval chain, which is copied onto the requisition at submission, and opened once every
/// step approves. A rejection returns it to draft with the approver's comment. Every status change is logged.
/// </summary>
public sealed class Requisition : Entity
{
    public const int MaxJobDescriptionLength = 20_000;
    public const int DefaultInterviewRounds = 2;
    public const int MaxInterviewRounds = 6;

    public static readonly IReadOnlyList<string> DefaultScorecardCriteria =
        ["Technical skills", "Problem solving", "Communication", "Team fit"];

    private readonly List<RequisitionApproval> _approvals = [];
    private readonly List<RequisitionStatusChange> _statusHistory = [];

    private Requisition()
    {
    }

    /// <summary>Human-friendly reference, e.g. REQ-00042.</summary>
    public string Number { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public Guid DepartmentId { get; private set; }

    public Guid OfficeLocationId { get; private set; }

    /// <summary>From the office; drives HR country scoping and the budget currency (MC-02).</summary>
    public string CountryCode { get; private set; } = null!;

    public int Openings { get; private set; }

    public decimal BudgetMin { get; private set; }

    public decimal BudgetMax { get; private set; }

    /// <summary>The country's currency (PKR or USD); never converted.</summary>
    public string CurrencyCode { get; private set; } = null!;

    public RequisitionReason Reason { get; private set; }

    public Guid? ReplacedEmployeeId { get; private set; }

    public DateOnly TargetDate { get; private set; }

    public string JobDescription { get; private set; } = null!;

    public RequisitionStatus Status { get; private set; }

    public Guid RequesterEmployeeId { get; private set; }

    /// <summary>Owns the hiring: sees the requisition's candidates (CT). Defaults to the requester; HR can name someone else.</summary>
    public Guid HiringManagerEmployeeId { get; private set; }

    /// <summary>Interview rounds in this requisition's pipeline (Interview 1..n).</summary>
    public int InterviewRounds { get; private set; } = DefaultInterviewRounds;

    /// <summary>CT-06: what interviewers rate, 1 to 5. Copied onto each interview when it is scheduled.</summary>
    public List<string> ScorecardCriteria { get; private set; } = [.. DefaultScorecardCriteria];

    /// <summary>Increments on each submission; approvals of earlier rounds are kept as history.</summary>
    public int ApprovalRound { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<RequisitionApproval> Approvals => _approvals;

    public IReadOnlyList<RequisitionStatusChange> StatusHistory => _statusHistory;

    public Money BudgetMinimum => new(BudgetMin, CurrencyCode);

    public Money BudgetMaximum => new(BudgetMax, CurrencyCode);

    /// <summary>The step waiting for a decision in the current round, if any.</summary>
    public RequisitionApproval? CurrentStep =>
        Status == RequisitionStatus.PendingApproval
            ? _approvals.Where(a => a.Round == ApprovalRound && a.Decision == ApprovalDecision.Pending).MinBy(a => a.StepOrder)
            : null;

    public static Requisition Create(string number, RequisitionDetails details, CountrySettings country, Guid requesterEmployeeId, Actor actor, DateTimeOffset now)
    {
        var requisition = new Requisition
        {
            Number = Guard.RequiredText(nameof(number), number, 20),
            RequesterEmployeeId = requesterEmployeeId,
            HiringManagerEmployeeId = requesterEmployeeId,
            Status = RequisitionStatus.Draft,
            CreatedAtUtc = now.ToUniversalTime(),
        };
        requisition.Apply(details, country);
        requisition.Log(null, RequisitionStatus.Draft, actor, now, null);
        return requisition;
    }

    /// <summary>Only drafts can be edited; withdraw a pending requisition first.</summary>
    public void Update(RequisitionDetails details, CountrySettings country)
    {
        EnsureStatus("edit", RequisitionStatus.Draft);
        Apply(details, country);
    }

    /// <summary>Starts a new approval round with a copy of the country's chain.</summary>
    public void Submit(IReadOnlyList<ApprovalStep> chain, Actor actor, DateOnly today, DateTimeOffset now)
    {
        EnsureStatus("submit", RequisitionStatus.Draft);
        if (chain.Count == 0)
        {
            throw new DomainValidationException("approvalChain", "No approval chain is set up for this country. Ask an HR Admin to set one up (by default, a single CEO approval).");
        }

        if (TargetDate < today)
        {
            throw new DomainValidationException("targetDate", "The target date has passed; choose a new one before submitting.");
        }

        ApprovalRound++;
        for (var i = 0; i < chain.Count; i++)
        {
            _approvals.Add(new RequisitionApproval(Id, ApprovalRound, i + 1, chain[i].Label, chain[i].ApproverEmployeeId));
        }

        Transition(RequisitionStatus.PendingApproval, actor, now, null);
    }

    /// <summary>The requester (or HR) pulls a pending requisition back to draft, e.g. to change it.</summary>
    public void Withdraw(Actor actor, DateTimeOffset now, string? comment)
    {
        EnsureStatus("withdraw", RequisitionStatus.PendingApproval);
        foreach (var pending in _approvals.Where(a => a.Round == ApprovalRound && a.Decision == ApprovalDecision.Pending))
        {
            pending.Close();
        }

        Transition(RequisitionStatus.Draft, actor, now, comment ?? "Withdrawn");
    }

    /// <summary>REQ-02: the current step's approver approves or rejects. Approving the last step opens the position.</summary>
    public DecisionOutcome Decide(Guid approverEmployeeId, bool approve, string? comment, Actor actor, DateTimeOffset now)
    {
        var step = CurrentStep ?? throw new DomainValidationException("status", "This requisition is not waiting for approval.");
        if (step.ApproverEmployeeId != approverEmployeeId)
        {
            throw new InvalidOperationException("Not the current approver.");
        }

        if (approverEmployeeId == RequesterEmployeeId)
        {
            throw new DomainValidationException("approver", "You cannot approve your own requisition. Ask an HR Admin to change the approval chain.");
        }

        if (!approve && string.IsNullOrWhiteSpace(comment))
        {
            throw new DomainValidationException("comment", "Say why you are rejecting it, so the requester can revise it.");
        }

        step.Record(approve, comment, now);
        if (!approve)
        {
            foreach (var later in _approvals.Where(a => a.Round == ApprovalRound && a.Decision == ApprovalDecision.Pending))
            {
                later.Close();
            }

            Transition(RequisitionStatus.Draft, actor, now, $"Rejected by {step.Label}: {step.Comment}");
            return new DecisionOutcome(false, false, null);
        }

        var next = CurrentStep;
        if (next is null)
        {
            Transition(RequisitionStatus.Open, actor, now, "Approved");
            return new DecisionOutcome(true, true, null);
        }

        return new DecisionOutcome(true, false, next);
    }

    /// <summary>Moves between Open, On hold, Filled and Cancelled (and cancels drafts or pending requests).</summary>
    public void ChangeStatus(RequisitionStatus status, Actor actor, DateTimeOffset now, string? comment)
    {
        var allowed = Status switch
        {
            RequisitionStatus.Draft => [RequisitionStatus.Cancelled],
            RequisitionStatus.PendingApproval => [RequisitionStatus.Cancelled],
            RequisitionStatus.Open => [RequisitionStatus.OnHold, RequisitionStatus.Filled, RequisitionStatus.Cancelled],
            RequisitionStatus.OnHold => new[] { RequisitionStatus.Open, RequisitionStatus.Cancelled },
            _ => [],
        };

        if (!allowed.Contains(status))
        {
            throw new DomainValidationException(nameof(status), $"A requisition that is {Label(Status)} cannot become {Label(status)}.");
        }

        if (Status == RequisitionStatus.PendingApproval)
        {
            foreach (var pending in _approvals.Where(a => a.Round == ApprovalRound && a.Decision == ApprovalDecision.Pending))
            {
                pending.Close();
            }
        }

        Transition(status, actor, now, Guard.OptionalText(nameof(comment), comment, 1000));
    }

    /// <summary>
    /// Interview rounds and scorecard criteria. <paramref name="roundsInUse"/> is the highest round that already has
    /// candidates or interviews; the pipeline cannot shrink below it.
    /// </summary>
    public void ConfigurePipeline(int interviewRounds, IReadOnlyList<string> criteria, int roundsInUse)
    {
        if (Status is RequisitionStatus.Filled or RequisitionStatus.Cancelled)
        {
            throw new DomainValidationException("status", $"This requisition is {Label(Status)}.");
        }

        Guard.Range(nameof(interviewRounds), interviewRounds, 1, MaxInterviewRounds, $"Choose between 1 and {MaxInterviewRounds} interview rounds.");
        if (interviewRounds < roundsInUse)
        {
            throw new DomainValidationException(nameof(interviewRounds), $"Round {roundsInUse} is already in use; keep at least {roundsInUse} rounds.");
        }

        var cleaned = criteria.Select(c => Guard.RequiredText("scorecardCriteria", c, 100)).ToList();
        if (cleaned.Count is 0 or > 10 || cleaned.Distinct(StringComparer.OrdinalIgnoreCase).Count() != cleaned.Count)
        {
            throw new DomainValidationException("scorecardCriteria", "Use between 1 and 10 different criteria.");
        }

        InterviewRounds = interviewRounds;
        ScorecardCriteria = cleaned;
    }

    /// <summary>REQ-03: candidates are only added to open positions.</summary>
    public bool AcceptsCandidates => Status == RequisitionStatus.Open;

    /// <summary>Statuses reachable through <see cref="ChangeStatus"/> from the current one.</summary>
    public IReadOnlyList<RequisitionStatus> ManualNextStatuses => Status switch
    {
        RequisitionStatus.Draft or RequisitionStatus.PendingApproval => [RequisitionStatus.Cancelled],
        RequisitionStatus.Open => [RequisitionStatus.OnHold, RequisitionStatus.Filled, RequisitionStatus.Cancelled],
        RequisitionStatus.OnHold => [RequisitionStatus.Open, RequisitionStatus.Cancelled],
        _ => [],
    };

    public static string Label(RequisitionStatus status) => status switch
    {
        RequisitionStatus.PendingApproval => "pending approval",
        RequisitionStatus.OnHold => "on hold",
        _ => status.ToString().ToLowerInvariant(),
    };

    private void Apply(RequisitionDetails d, CountrySettings country)
    {
        if (d.OfficeLocation.CountryCode != country.CountryCode)
        {
            throw new ArgumentException("Country does not match the office.", nameof(country));
        }

        if (d.BudgetMin.Currency != country.CurrencyCode || d.BudgetMax.Currency != country.CurrencyCode)
        {
            throw new DomainValidationException("currencyCode", $"Budgets for {country.Name} are in {country.CurrencyCode}.");
        }

        if (d.BudgetMin.Amount <= 0 || d.BudgetMax.Amount < d.BudgetMin.Amount || decimal.Round(d.BudgetMax.Amount, 2) != d.BudgetMax.Amount
            || decimal.Round(d.BudgetMin.Amount, 2) != d.BudgetMin.Amount || d.BudgetMax.Amount >= 1_000_000_000_000m)
        {
            throw new DomainValidationException("budgetMax", "Enter a budget range from a positive minimum up to a maximum at least as high.");
        }

        if (d.Reason == RequisitionReason.Replacement && d.ReplacedEmployee is null)
        {
            throw new DomainValidationException("replacedEmployeeId", "Choose the employee being replaced.");
        }

        if (d.Reason == RequisitionReason.NewPosition && d.ReplacedEmployee is not null)
        {
            throw new DomainValidationException("replacedEmployeeId", "A new position does not replace anyone.");
        }

        if (d.ReplacedEmployee is { } replaced && replaced.DepartmentId != d.Department.Id)
        {
            throw new DomainValidationException("replacedEmployeeId", "The employee being replaced must be in the requisition's department.");
        }

        if (!d.Department.IsActive || !d.OfficeLocation.IsActive)
        {
            throw new DomainValidationException("departmentId", "Choose an active department and office.");
        }

        Title = Guard.RequiredText("title", d.Title, 200);
        DepartmentId = d.Department.Id;
        OfficeLocationId = d.OfficeLocation.Id;
        CountryCode = country.CountryCode;
        Openings = Guard.Range("openings", d.Openings, 1, 100, "Openings must be between 1 and 100.");
        BudgetMin = d.BudgetMin.Amount;
        BudgetMax = d.BudgetMax.Amount;
        CurrencyCode = country.CurrencyCode;
        Reason = d.Reason;
        ReplacedEmployeeId = d.ReplacedEmployee?.Id;
        if (d.HiringManager is { } hiringManager)
        {
            if (!hiringManager.Status.IsActive())
            {
                throw new DomainValidationException("hiringManagerEmployeeId", "The hiring manager has left the company.");
            }

            HiringManagerEmployeeId = hiringManager.Id;
        }

        TargetDate = d.TargetDate;
        JobDescription = Guard.RequiredText("jobDescription", d.JobDescription, MaxJobDescriptionLength);
    }

    private void EnsureStatus(string action, RequisitionStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainValidationException("status", $"Only a requisition that is {Label(expected)} can {action}; this one is {Label(Status)}.");
        }
    }

    private void Transition(RequisitionStatus to, Actor actor, DateTimeOffset now, string? comment)
    {
        var from = Status;
        Status = to;
        Log(from, to, actor, now, comment);
    }

    private void Log(RequisitionStatus? from, RequisitionStatus to, Actor actor, DateTimeOffset now, string? comment) =>
        _statusHistory.Add(new RequisitionStatusChange(Id, from, to, actor, now, comment));
}
