using System.Net.Http.Json;
using HrPortal.Domain.Employees;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Employees.EmployeeEndpoints;

namespace HrPortal.IntegrationTests.Infrastructure;

/// <summary>
/// Throwaway employees for tests that change data, so the seeded population (see <see cref="SeedData"/>) stays as
/// scope tests expect. Their numbers start with "T-" and never match the seeded "PK-0001" pattern.
/// </summary>
public static class TestEmployees
{
    public static string NewNumber() => $"T-{Guid.NewGuid().ToString("N")[..10]}".ToUpperInvariant();

    public static async Task<CreateEmployeeRequest> NewRequestAsync(
        this HrPortalApiFactory factory, string country = "PK", Guid? managerId = null, PersonalDetailsDto? personal = null)
    {
        var ids = await factory.WithDbAsync(async db => new
        {
            LegalEntity = await db.LegalEntities.Where(e => e.CountryCode == country && e.Name.StartsWith("HR Portal Dev")).Select(e => e.Id).FirstAsync(),
            Office = await db.OfficeLocations.Where(e => e.CountryCode == country && e.City != null).OrderBy(e => e.Name).Select(e => e.Id).FirstAsync(),
            Department = await db.Departments.Where(d => d.Code == "ENG").Select(d => d.Id).SingleAsync(),
            Grade = await db.Grades.Where(g => g.CountryCode == country && g.Code == "G1").Select(g => g.Id).SingleAsync(),
        });

        var number = NewNumber();
        return new CreateEmployeeRequest(
            "Test", $"Person {number}", number, $"{number.ToLowerInvariant()}@hrportal.test", EmploymentType.Permanent,
            "Test Engineer", ids.LegalEntity, ids.Office, ids.Department, ids.Grade, managerId, new DateOnly(2025, 1, 6), personal);
    }

    /// <summary>Creates an employee through the API as <paramref name="asUser"/> (an HR Admin by default).</summary>
    public static async Task<EmployeeDetail> CreateEmployeeAsync(
        this HrPortalApiFactory factory, string country = "PK", Guid? managerId = null, PersonalDetailsDto? personal = null, string asUser = "dev-us-hradmin")
    {
        var response = await factory.CreateClientAs(asUser).PostAsJsonAsync("/api/employees", await factory.NewRequestAsync(country, managerId, personal));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeeDetail>())!;
    }
}
