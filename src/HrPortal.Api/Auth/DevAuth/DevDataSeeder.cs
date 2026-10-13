using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Auth.DevAuth;

/// <summary>
/// Seeds fictional organisation data and an employee record for every <see cref="DevUsers">dev user</see>, so data
/// scoping can be exercised locally and in tests. Runs only when DevAuth is enabled (Development/Test).
/// </summary>
/// <remarks>
/// Reporting lines, the same in each country:
/// <code>
/// HrAdmin ── HrExecutive              SystemAdmin (IT)      Finance Analyst (FIN, no login)
/// HiringManager (ENG) ─┬─ LineManager ── Employee ── Junior Engineer (no login)
///                      └─ Interviewer
/// </code>
/// </remarks>
public static class DevDataSeeder
{
    public static async Task SeedAsync(HrPortalDbContext db, CancellationToken ct = default)
    {
        if (await db.Employees.AnyAsync(e => e.UserObjectId == DevUsers.All[0].Id, ct))
        {
            return;
        }

        var departments = new Dictionary<string, Department>();
        foreach (var (code, name) in new[] { ("ENG", "Engineering"), ("HR", "Human Resources"), ("FIN", "Finance"), ("IT", "Information Technology") })
        {
            departments[code] = await db.Departments.SingleOrDefaultAsync(d => d.Code == code, ct)
                ?? db.Departments.Add(new Department(code, name)).Entity;
        }

        foreach (var country in new[] { "PK", "US" })
        {
            var legalEntity = db.LegalEntities.Add(new LegalEntity(country, country == "PK" ? "HR Portal Dev (Pvt) Ltd" : "HR Portal Dev Inc.")).Entity;
            var office = db.OfficeLocations.Add(country == "PK"
                ? new OfficeLocation("PK", "Lahore Office", "Lahore", null)
                : new OfficeLocation("US", "US Office", "New York", null)).Entity;
            if (country == "PK")
            {
                db.OfficeLocations.Add(new OfficeLocation("PK", "Karachi Office", "Karachi", null));
            }

            var associate = db.Grades.Add(new Grade(country, "G1", "Associate", 1, null, null)).Entity;
            var senior = db.Grades.Add(new Grade(country, "G2", "Senior", 2, null, null)).Entity;
            var manager = db.Grades.Add(new Grade(country, "G3", "Manager", 3, null, 60)).Entity;

            var number = 0;
            Employee Add(string givenName, string familyName, string email, string jobTitle, string department, Grade grade, Employee? reportsTo, string? userId)
            {
                var employee = Employee.Create(
                    new Person(givenName, familyName),
                    $"{country}-{++number:D4}",
                    email,
                    jobTitle,
                    legalEntity,
                    office,
                    departments[department],
                    grade,
                    new DateOnly(2024, 1, 1).AddMonths(number));
                employee.SetManager(reportsTo);
                employee.LinkUserAccount(userId);
                db.People.Add(employee.Person);
                db.Employees.Add(employee);
                return employee;
            }

            Employee AddDevUser(string role, string jobTitle, string department, Grade grade, Employee? reportsTo)
            {
                var user = DevUsers.All.Single(u => u.CountryCode == country && u.Roles[0] == role);
                return Add("Dev", user.DisplayName["Dev ".Length..], user.Email, jobTitle, department, grade, reportsTo, user.Id);
            }

            var hrAdmin = AddDevUser(Roles.HrAdmin, "Head of HR", "HR", manager, null);
            AddDevUser(Roles.HrExecutive, "HR Executive", "HR", senior, hrAdmin);
            AddDevUser(Roles.SystemAdmin, "IT Administrator", "IT", senior, null);
            var hiringManager = AddDevUser(Roles.HiringManager, "Head of Engineering", "ENG", manager, null);
            var lineManager = AddDevUser(Roles.LineManager, "Engineering Manager", "ENG", manager, hiringManager);
            var employee = AddDevUser(Roles.Employee, "Software Engineer", "ENG", senior, lineManager);
            AddDevUser(Roles.Interviewer, "Senior Engineer", "ENG", senior, hiringManager);
            var cc = country.ToLowerInvariant();
            Add("Junior", $"Engineer ({country})", $"junior.engineer.{cc}@hrportal.test", "Junior Engineer", "ENG", associate, employee, null);
            Add("Finance", $"Analyst ({country})", $"finance.analyst.{cc}@hrportal.test", "Finance Analyst", "FIN", associate, null, null);
        }

        await db.SaveChangesAsync(ct);
    }
}
