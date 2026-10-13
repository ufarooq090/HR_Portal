using System.Text.Json.Serialization;
using HrPortal.Domain.Common;

namespace HrPortal.Domain.Requisitions;

[JsonConverter(typeof(JsonStringEnumConverter<ApprovalDecision>))]
public enum ApprovalDecision
{
    Pending,
    Approved,
    Rejected,

    /// <summary>Never decided: the round ended (rejection, withdrawal or cancellation) before this step.</summary>
    Closed,
}

/// <summary>One step of an approval round, copied from the country's chain at submission.</summary>
public sealed class RequisitionApproval : Entity
{
    private RequisitionApproval()
    {
    }

    internal RequisitionApproval(Guid requisitionId, int round, int stepOrder, string label, Guid approverEmployeeId)
    {
        RequisitionId = requisitionId;
        Round = round;
        StepOrder = stepOrder;
        Label = label;
        ApproverEmployeeId = approverEmployeeId;
        Decision = ApprovalDecision.Pending;
    }

    public Guid RequisitionId { get; private set; }

    public int Round { get; private set; }

    public int StepOrder { get; private set; }

    /// <summary>E.g. "CEO".</summary>
    public string Label { get; private set; } = null!;

    public Guid ApproverEmployeeId { get; private set; }

    public ApprovalDecision Decision { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    internal void Record(bool approve, string? comment, DateTimeOffset now)
    {
        Decision = approve ? ApprovalDecision.Approved : ApprovalDecision.Rejected;
        Comment = Guard.OptionalText("comment", comment, 1000);
        DecidedAtUtc = now.ToUniversalTime();
    }

    internal void Close() => Decision = ApprovalDecision.Closed;
}

/// <summary>REQ-04: every status change, with who made it and why.</summary>
public sealed class RequisitionStatusChange : Entity
{
    private RequisitionStatusChange()
    {
    }

    internal RequisitionStatusChange(Guid requisitionId, RequisitionStatus? from, RequisitionStatus to, Actor actor, DateTimeOffset at, string? comment)
    {
        RequisitionId = requisitionId;
        FromStatus = from;
        ToStatus = to;
        ActorId = actor.UserId;
        ActorName = actor.Name;
        AtUtc = at.ToUniversalTime();
        Comment = comment is { Length: > 1000 } ? comment[..1000] : comment;
    }

    public Guid RequisitionId { get; private set; }

    public RequisitionStatus? FromStatus { get; private set; }

    public RequisitionStatus ToStatus { get; private set; }

    public string ActorId { get; private set; } = null!;

    public string ActorName { get; private set; } = null!;

    public DateTimeOffset AtUtc { get; private set; }

    public string? Comment { get; private set; }
}
