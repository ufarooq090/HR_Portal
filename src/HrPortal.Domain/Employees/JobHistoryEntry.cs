using System.Text.Json.Serialization;
using HrPortal.Domain.Common;

namespace HrPortal.Domain.Employees;

[JsonConverter(typeof(JsonStringEnumConverter<JobChangeKind>))]
public enum JobChangeKind
{
    Joined,
    Promotion,
    Transfer,
    ManagerChange,
    Redesignation,
    Correction,
    StatusChange,
}

/// <summary>
/// EMP-05: a dated snapshot of an employee's job after a change (promotion, transfer, new manager, status).
/// Comparing consecutive snapshots shows what changed. Salary history lives in <see cref="Compensation"/>.
/// </summary>
public sealed class JobHistoryEntry : Entity
{
    private JobHistoryEntry()
    {
    }

    internal JobHistoryEntry(Employee employee, JobChangeKind kind, DateOnly effectiveDate, string? reason)
    {
        EmployeeId = employee.Id;
        Kind = kind;
        EffectiveDate = effectiveDate;
        Reason = Guard.OptionalText("reason", reason, 500);
        Designation = employee.Designation;
        DepartmentId = employee.DepartmentId;
        OfficeLocationId = employee.OfficeLocationId;
        LegalEntityId = employee.LegalEntityId;
        GradeId = employee.GradeId;
        ManagerId = employee.ManagerId;
        Status = employee.Status;
    }

    public Guid EmployeeId { get; private set; }

    public JobChangeKind Kind { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    public string? Reason { get; private set; }

    public string Designation { get; private set; } = null!;

    public Guid DepartmentId { get; private set; }

    public Guid OfficeLocationId { get; private set; }

    public Guid LegalEntityId { get; private set; }

    public Guid? GradeId { get; private set; }

    public Guid? ManagerId { get; private set; }

    public EmploymentStatus Status { get; private set; }
}
