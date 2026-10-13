using System.Text.Json.Serialization;
using HrPortal.Domain.Common;

namespace HrPortal.Domain.Employees;

[JsonConverter(typeof(JsonStringEnumConverter<ScheduledChangeStatus>))]
public enum ScheduledChangeStatus
{
    /// <summary>Shown as "Upcoming"; HR can still edit or cancel it.</summary>
    Pending,
    Applied,
    Cancelled,

    /// <summary>Could not be applied on the effective date (e.g. it would create a reporting loop); see the outcome.</summary>
    Failed,
}

/// <summary>
/// A future-dated job or manager change (HR decision 13 Oct 2026). It is applied automatically on its effective
/// date, in the employee's local time, by a background job, which records it in the job history (EMP-05). The
/// reporting-loop check runs both when scheduling and when applying.
/// </summary>
public sealed class ScheduledJobChange : Entity
{
    private ScheduledJobChange()
    {
    }

    public Guid EmployeeId { get; private set; }

    public JobChangeKind Kind { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    public string? Reason { get; private set; }

    public string? Designation { get; private set; }

    public Guid? LegalEntityId { get; private set; }

    public Guid? OfficeLocationId { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public Guid? GradeId { get; private set; }

    /// <summary>For manager changes: the new manager, or null to remove the manager.</summary>
    public Guid? NewManagerId { get; private set; }

    public ScheduledChangeStatus Status { get; private set; }

    /// <summary>Why it failed or was cancelled.</summary>
    public string? Outcome { get; private set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public bool IsManagerChange => Kind == JobChangeKind.ManagerChange;

    public static ScheduledJobChange ForJob(
        Employee employee, JobChangeKind kind, JobPlacement placement, DateOnly effectiveDate, string? reason, DateOnly today)
    {
        if (kind is not (JobChangeKind.Promotion or JobChangeKind.Transfer or JobChangeKind.Redesignation or JobChangeKind.Correction))
        {
            throw new DomainValidationException(nameof(kind), "Choose promotion, transfer, redesignation or correction.");
        }

        var change = new ScheduledJobChange { EmployeeId = employee.Id, Kind = kind, Status = ScheduledChangeStatus.Pending };
        change.UpdateJob(employee, placement, effectiveDate, reason, today);
        return change;
    }

    public static ScheduledJobChange ForManager(
        Employee employee, Employee? manager, bool managerReportsToThisEmployee, DateOnly effectiveDate, string? reason, DateOnly today)
    {
        var change = new ScheduledJobChange { EmployeeId = employee.Id, Kind = JobChangeKind.ManagerChange, Status = ScheduledChangeStatus.Pending };
        change.UpdateManager(employee, manager, managerReportsToThisEmployee, effectiveDate, reason, today);
        return change;
    }

    public void UpdateJob(Employee employee, JobPlacement placement, DateOnly effectiveDate, string? reason, DateOnly today)
    {
        EnsureEditable(employee, effectiveDate, today);
        if (IsManagerChange)
        {
            throw new InvalidOperationException("This is a manager change.");
        }

        Employee.ValidatePlacement(placement);
        Designation = Guard.RequiredText("designation", placement.Designation, 200);
        LegalEntityId = placement.LegalEntity.Id;
        OfficeLocationId = placement.OfficeLocation.Id;
        DepartmentId = placement.Department.Id;
        GradeId = placement.Grade?.Id;
        EffectiveDate = effectiveDate;
        Reason = Guard.OptionalText("reason", reason, 500);
    }

    public void UpdateManager(Employee employee, Employee? manager, bool managerReportsToThisEmployee, DateOnly effectiveDate, string? reason, DateOnly today)
    {
        EnsureEditable(employee, effectiveDate, today);
        if (!IsManagerChange)
        {
            throw new InvalidOperationException("This is not a manager change.");
        }

        if (manager?.Id == employee.Id)
        {
            throw new DomainValidationException("managerId", "An employee cannot report to themselves.");
        }

        if (manager is not null && !manager.Status.IsActive())
        {
            throw new DomainValidationException("managerId", "The manager has left the company.");
        }

        if (managerReportsToThisEmployee)
        {
            throw new DomainValidationException("managerId", "The new manager reports to this employee; that would create a reporting loop.");
        }

        NewManagerId = manager?.Id;
        EffectiveDate = effectiveDate;
        Reason = Guard.OptionalText("reason", reason, 500);
    }

    public void Cancel(string reason, DateTimeOffset at)
    {
        EnsurePending();
        Status = ScheduledChangeStatus.Cancelled;
        Outcome = Guard.OptionalText("reason", reason, 500);
        ResolvedAtUtc = at.ToUniversalTime();
    }

    public void MarkApplied(DateTimeOffset at)
    {
        EnsurePending();
        Status = ScheduledChangeStatus.Applied;
        ResolvedAtUtc = at.ToUniversalTime();
    }

    public void MarkFailed(string reason, DateTimeOffset at)
    {
        EnsurePending();
        Status = ScheduledChangeStatus.Failed;
        Outcome = reason.Length <= 500 ? reason : reason[..500];
        ResolvedAtUtc = at.ToUniversalTime();
    }

    private void EnsureEditable(Employee employee, DateOnly effectiveDate, DateOnly today)
    {
        EnsurePending();
        if (employee.Id != EmployeeId)
        {
            throw new ArgumentException("Wrong employee.", nameof(employee));
        }

        if (!employee.Status.IsActive())
        {
            throw new DomainValidationException("status", $"The employee has left ({employee.Status}); their job can no longer change.");
        }

        if (effectiveDate <= today)
        {
            throw new DomainValidationException("effectiveDate", "A scheduled change must take effect after today.");
        }
    }

    private void EnsurePending()
    {
        if (Status != ScheduledChangeStatus.Pending)
        {
            throw new DomainValidationException("status", $"This change is already {Status.ToString().ToLowerInvariant()} and can no longer be changed.");
        }
    }
}
