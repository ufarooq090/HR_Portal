using HrPortal.Domain.Common;
using HrPortal.Domain.Organisation;

namespace HrPortal.Domain.Employees;

/// <summary>
/// A person while employed. Every employee belongs to a country, legal entity and office location (MC-01),
/// and the country of all three must agree. Data scoping (own / team / department / country) is evaluated
/// against these links.
/// </summary>
public sealed class Employee : Entity
{
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

    public string JobTitle { get; private set; } = null!;

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

    public static Employee Create(
        Person person,
        string employeeNumber,
        string workEmail,
        string jobTitle,
        LegalEntity legalEntity,
        OfficeLocation officeLocation,
        Department department,
        Grade? grade,
        DateOnly joiningDate)
    {
        var employee = new Employee
        {
            PersonId = person.Id,
            Person = person,
            EmployeeNumber = Guard.RequiredText(nameof(employeeNumber), employeeNumber, 32).ToUpperInvariant(),
            WorkEmail = Guard.RequiredText(nameof(workEmail), workEmail, 256).ToLowerInvariant(),
            JoiningDate = joiningDate,
        };
        employee.Assign(jobTitle, legalEntity, officeLocation, department, grade);
        return employee;
    }

    /// <summary>Sets job and organisation placement. Legal entity, office and grade must share one country.</summary>
    public void Assign(string jobTitle, LegalEntity legalEntity, OfficeLocation officeLocation, Department department, Grade? grade)
    {
        if (officeLocation.CountryCode != legalEntity.CountryCode)
        {
            throw new DomainValidationException(
                nameof(officeLocation), "The office must be in the same country as the legal entity.");
        }

        if (grade is not null && grade.CountryCode != legalEntity.CountryCode)
        {
            throw new DomainValidationException(nameof(grade), "The grade must belong to the employee's country.");
        }

        JobTitle = Guard.RequiredText(nameof(jobTitle), jobTitle, 200);
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

    public void SetManager(Employee? manager)
    {
        if (manager?.Id == Id)
        {
            throw new DomainValidationException(nameof(manager), "An employee cannot report to themselves.");
        }

        Manager = manager;
        ManagerId = manager?.Id;
    }

    public void LinkUserAccount(string? userObjectId) =>
        UserObjectId = Guard.OptionalText(nameof(userObjectId), userObjectId, 128);
}
