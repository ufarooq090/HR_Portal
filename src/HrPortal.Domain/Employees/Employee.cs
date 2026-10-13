using HrPortal.Domain.Common;
using HrPortal.Domain.Organisation;

namespace HrPortal.Domain.Employees;

/// <summary>Job placement for <see cref="Employee.Create"/> and <see cref="Employee.ChangeJob"/>.</summary>
public sealed record JobPlacement(
    string Designation,
    LegalEntity LegalEntity,
    OfficeLocation OfficeLocation,
    Department Department,
    Grade? Grade);

/// <summary>
/// A person while employed (EMP-02). Every employee belongs to a country, legal entity and office location
/// (MC-01), and the country of all three must agree. Data scoping (own / team / department / country) is
/// evaluated against these links. Every job or status change appends a dated <see cref="JobHistoryEntry"/>.
/// </summary>
public sealed class Employee : Entity
{
    private readonly List<JobHistoryEntry> _history = [];

    private Employee()
    {
    }

    public Guid PersonId { get; private set; }

    public Person Person { get; private set; } = null!;

    /// <summary>Company employee ID, e.g. PK-0042. Unique.</summary>
    public string EmployeeNumber { get; private set; } = null!;

    public string WorkEmail { get; private set; } = null!;

    /// <summary>Entra ID object id of the employee's sign-in account; links the signed-in user to this record.</summary>
    public string? UserObjectId { get; private set; }

    public string Designation { get; private set; } = null!;

    public EmploymentType EmploymentType { get; private set; }

    public EmploymentStatus Status { get; private set; }

    /// <summary>Denormalised from the legal entity and office so country scoping is a simple column filter.</summary>
    public string CountryCode { get; private set; } = null!;

    public Guid LegalEntityId { get; private set; }

    public LegalEntity LegalEntity { get; private set; } = null!;

    public Guid OfficeLocationId { get; private set; }

    public OfficeLocation OfficeLocation { get; private set; } = null!;

    public Guid DepartmentId { get; private set; }

    public Department Department { get; private set; } = null!;

    public Guid? GradeId { get; private set; }

    public Grade? Grade { get; private set; }

    /// <summary>Reporting manager. Team scope covers direct and indirect reports.</summary>
    public Guid? ManagerId { get; private set; }

    public Employee? Manager { get; private set; }

    public DateOnly JoiningDate { get; private set; }

    /// <summary>Set at joining from the country/grade probation period; null when there is no probation.</summary>
    public DateOnly? ProbationEndDate { get; private set; }

    /// <summary>Set when notice starts or the employee leaves.</summary>
    public DateOnly? LastWorkingDate { get; private set; }

    public IReadOnlyList<JobHistoryEntry> History => _history;

    public static Employee Create(
        Person person,
        string employeeNumber,
        string workEmail,
        EmploymentType employmentType,
        JobPlacement placement,
        Employee? manager,
        DateOnly joiningDate,
        EmploymentTerms terms)
    {
        if (manager is not null && !manager.Status.IsActive())
        {
            throw new DomainValidationException("managerId", "The manager has left the company.");
        }

        var employee = new Employee
        {
            PersonId = person.Id,
            Person = person,
            EmployeeNumber = Guard.RequiredText(nameof(employeeNumber), employeeNumber, 32).ToUpperInvariant(),
            WorkEmail = Person.RequiredEmail(nameof(workEmail), workEmail),
            EmploymentType = employmentType,
            JoiningDate = joiningDate,
            ProbationEndDate = terms.ProbationMonths > 0 ? joiningDate.AddMonths(terms.ProbationMonths) : null,
            Status = terms.ProbationMonths > 0 ? EmploymentStatus.Probation : EmploymentStatus.Confirmed,
        };
        employee.Place(placement);
        employee.SetManager(manager);
        employee._history.Add(new JobHistoryEntry(employee, JobChangeKind.Joined, joiningDate, null));
        return employee;
    }

    /// <summary>EMP-05: promotion, transfer, redesignation or correction, effective on a past or current date.</summary>
    public void ChangeJob(JobChangeKind kind, JobPlacement placement, DateOnly effectiveDate, string? reason, DateOnly today)
    {
        if (kind is JobChangeKind.Joined or JobChangeKind.StatusChange or JobChangeKind.ManagerChange)
        {
            throw new DomainValidationException(nameof(kind), "Choose promotion, transfer, redesignation or correction.");
        }

        EnsureActive();
        EnsureEffectiveDate(effectiveDate, today);

        var before = (Designation, LegalEntityId, OfficeLocationId, DepartmentId, GradeId);
        Place(placement);
        if (before == (Designation, LegalEntityId, OfficeLocationId, DepartmentId, GradeId))
        {
            throw new DomainValidationException("designation", "Nothing changed.");
        }

        _history.Add(new JobHistoryEntry(this, kind, effectiveDate, reason));
    }

    /// <summary>
    /// EMP-05: new reporting manager. <paramref name="managerReportsToThisEmployee"/> must say whether the new
    /// manager is in this employee's own reporting line, which would create a cycle.
    /// </summary>
    public void ChangeManager(Employee? manager, bool managerReportsToThisEmployee, DateOnly effectiveDate, string? reason, DateOnly today)
    {
        EnsureActive();
        EnsureEffectiveDate(effectiveDate, today);
        if (manager?.Id == ManagerId)
        {
            throw new DomainValidationException("managerId", "Nothing changed.");
        }

        if (manager is not null && !manager.Status.IsActive())
        {
            throw new DomainValidationException("managerId", "The manager has left the company.");
        }

        if (managerReportsToThisEmployee)
        {
            throw new DomainValidationException("managerId", "The new manager reports to this employee; that would create a reporting loop.");
        }

        SetManager(manager);
        _history.Add(new JobHistoryEntry(this, JobChangeKind.ManagerChange, effectiveDate, reason));
    }

    /// <summary>EMP-04. Starting notice sets the last working day from the notice period unless one is given.</summary>
    public void ChangeStatus(
        EmploymentStatus status, DateOnly effectiveDate, DateOnly? lastWorkingDate, EmploymentTerms terms, string? reason, DateOnly today)
    {
        if (!EmploymentStatusRules.CanChange(Status, status))
        {
            throw new DomainValidationException(nameof(status), $"An employee who is {Status} cannot become {status}.");
        }

        EnsureEffectiveDate(effectiveDate, today);

        switch (status)
        {
            case EmploymentStatus.OnNotice:
                var last = lastWorkingDate ?? effectiveDate.AddDays(terms.NoticePeriodDays);
                if (last < effectiveDate)
                {
                    throw new DomainValidationException(nameof(lastWorkingDate), "The last working day cannot be before notice starts.");
                }

                LastWorkingDate = last;
                break;
            case EmploymentStatus.Confirmed:
                LastWorkingDate = null;
                break;
            default:
                LastWorkingDate = lastWorkingDate ?? effectiveDate;
                break;
        }

        Status = status;
        _history.Add(new JobHistoryEntry(this, JobChangeKind.StatusChange, effectiveDate, reason));
    }

    private void SetManager(Employee? manager)
    {
        if (manager?.Id == Id)
        {
            throw new DomainValidationException("managerId", "An employee cannot report to themselves.");
        }

        Manager = manager;
        ManagerId = manager?.Id;
    }

    public void LinkUserAccount(string? userObjectId) =>
        UserObjectId = Guard.OptionalText(nameof(userObjectId), userObjectId, 128);

    /// <summary>Legal entity, office and grade must share one country (MC-01).</summary>
    public static void ValidatePlacement(JobPlacement placement)
    {
        if (placement.OfficeLocation.CountryCode != placement.LegalEntity.CountryCode)
        {
            throw new DomainValidationException("officeLocationId", "The office must be in the same country as the legal entity.");
        }

        if (placement.Grade is not null && placement.Grade.CountryCode != placement.LegalEntity.CountryCode)
        {
            throw new DomainValidationException("gradeId", "The grade must belong to the employee's country.");
        }
    }

    private void Place(JobPlacement placement)
    {
        ValidatePlacement(placement);
        var (designation, legalEntity, officeLocation, department, grade) = placement;
        Designation = Guard.RequiredText(nameof(designation), designation, 200);
        CountryCode = legalEntity.CountryCode;
        LegalEntity = legalEntity;
        LegalEntityId = legalEntity.Id;
        OfficeLocation = officeLocation;
        OfficeLocationId = officeLocation.Id;
        Department = department;
        DepartmentId = department.Id;
        Grade = grade;
        GradeId = grade?.Id;
    }

    private void EnsureActive()
    {
        if (!Status.IsActive())
        {
            throw new DomainValidationException("status", $"The employee has left ({Status}); their job can no longer change.");
        }
    }

    private void EnsureEffectiveDate(DateOnly effectiveDate, DateOnly today)
    {
        if (effectiveDate < JoiningDate)
        {
            throw new DomainValidationException(nameof(effectiveDate), "The change cannot be before the joining date.");
        }

        if (effectiveDate > today)
        {
            throw new DomainValidationException(nameof(effectiveDate), "Record changes once they take effect; future dates are not supported yet.");
        }
    }
}
