using System.Net;
using System.Net.Http.Json;
using HrPortal.Api.Auth.DevAuth;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Security;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Employees.EmployeeEndpoints;

namespace HrPortal.IntegrationTests.Employees;

/// <summary>Data scoping for employee records (NFR-02, MC-03), for every seeded role in both countries.</summary>
[Collection(ApiCollection.Name)]
public class EmployeeScopeTests(HrPortalApiFactory factory)
{
    [Theory]
    [MemberData(nameof(SeedData.DevUsersData), MemberType = typeof(SeedData))]
    public async Task NFR_02_directory_returns_exactly_the_users_scope(string devUser)
    {
        var employees = await factory.CreateClientAs(devUser).GetFromJsonAsync<List<EmployeeSummary>>("/api/employees");

        Assert.Equal(SeedData.VisibleTo(devUser), employees!.Select(e => e.EmployeeNumber).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(SeedData.DevUsersData), MemberType = typeof(SeedData))]
    public async Task NFR_02_reading_an_out_of_scope_employee_is_refused_with_403(string devUser)
    {
        var client = factory.CreateClientAs(devUser);
        var visible = SeedData.VisibleTo(devUser);
        var allNumbers = await factory.WithDbAsync(db => db.Employees.Select(e => e.EmployeeNumber).ToListAsync());

        foreach (var number in allNumbers.Where(n => n.Length == 7))
        {
            var response = await client.GetAsync($"/api/employees/{await factory.EmployeeIdAsync(number)}");

            var expected = visible.Contains(number) ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            Assert.True(expected == response.StatusCode, $"{devUser} reading {number}: expected {expected}, got {response.StatusCode}");
        }
    }

    [Fact]
    public async Task Line_manager_sees_indirect_reports_but_an_employee_does_not_see_their_own_report()
    {
        // The junior engineer reports to dev-pk-employee, who has no manager role: scope is by role first, then team.
        var junior = await factory.EmployeeIdAsync("PK-0008");

        var lineManager = await factory.CreateClientAs("dev-pk-linemanager").GetAsync($"/api/employees/{junior}");
        var employee = await factory.CreateClientAs("dev-pk-employee").GetAsync($"/api/employees/{junior}");

        Assert.Equal(HttpStatusCode.OK, lineManager.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);
    }

    [Fact]
    public async Task Search_never_widens_the_scope()
    {
        var results = await factory.CreateClientAs("dev-pk-linemanager")
            .GetFromJsonAsync<List<EmployeeSummary>>("/api/employees?search=finance");

        Assert.Empty(results!);
    }

    [Fact]
    public async Task Search_matches_name_number_and_email_within_scope()
    {
        var client = factory.CreateClientAs("dev-pk-hrexecutive");

        var byName = await client.GetFromJsonAsync<List<EmployeeSummary>>("/api/employees?search=junior");
        var byNumber = await client.GetFromJsonAsync<List<EmployeeSummary>>("/api/employees?search=pk-0009");
        var wildcard = await client.GetFromJsonAsync<List<EmployeeSummary>>("/api/employees?search=%25");

        Assert.Equal(["PK-0008"], byName!.Select(e => e.EmployeeNumber));
        Assert.Equal(["PK-0009"], byNumber!.Select(e => e.EmployeeNumber));
        Assert.Empty(wildcard!);
    }

    [Fact]
    public async Task Unknown_employee_returns_404()
    {
        var response = await factory.CreateClientAs("dev-pk-hradmin").GetAsync($"/api/employees/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Directory_shows_department_office_and_manager()
    {
        var employees = await factory.CreateClientAs("dev-us-linemanager").GetFromJsonAsync<List<EmployeeSummary>>("/api/employees");

        var employee = employees!.Single(e => e.EmployeeNumber == "US-0006");
        Assert.Equal("Dev Employee (US)", employee.FullName);
        Assert.Equal("Engineering", employee.DepartmentName);
        Assert.Equal("US Office", employee.OfficeLocationName);
        Assert.Equal("Dev LineManager (US)", employee.ManagerName);
    }

    [Fact]
    public async Task MC_03_HR_executive_granted_another_country_sees_it_and_loses_it_when_reset()
    {
        var client = factory.CreateClientAs("dev-pk-hrexecutive");
        var usEmployee = await factory.EmployeeIdAsync("US-0006");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/employees/{usEmployee}")).StatusCode);

        await using (await factory.SetHrAccessAsync("dev-pk-hrexecutive", CountryScope.Of(["PK", "US"])))
        {
            var all = await client.GetFromJsonAsync<List<EmployeeSummary>>("/api/employees");
            Assert.Equal(18, all!.Count(e => e.EmployeeNumber.Length == 7));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/employees/{usEmployee}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/employees/{usEmployee}")).StatusCode);
    }

    [Fact]
    public async Task MC_03_HR_admin_limited_to_one_country_sees_only_that_country()
    {
        await using var _ = await factory.SetHrAccessAsync("dev-us-hradmin", CountryScope.Of(["US"]));

        var employees = await factory.CreateClientAs("dev-us-hradmin").GetFromJsonAsync<List<EmployeeSummary>>("/api/employees");

        Assert.All(employees!, e => Assert.Equal("US", e.CountryCode));
    }

    [Fact]
    public async Task Department_scope_covers_the_department_in_every_country()
    {
        var numbers = await QueryAsAsync("dev-pk-hiringmanager", [DataScope.Department]);

        Assert.Equal(
            ["PK-0004", "PK-0005", "PK-0006", "PK-0007", "PK-0008", "US-0004", "US-0005", "US-0006", "US-0007", "US-0008"],
            numbers);
    }

    [Fact]
    public async Task Assigned_scope_grants_no_employee_records()
    {
        Assert.Empty(await QueryAsAsync("dev-pk-interviewer", [DataScope.Assigned]));
    }

    [Fact]
    public async Task Team_scope_terminates_on_a_reporting_cycle()
    {
        await factory.WithDbAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var employee = await db.Employees.SingleAsync(e => e.EmployeeNumber == "PK-0006");
            var lineManager = await db.Employees.SingleAsync(e => e.EmployeeNumber == "PK-0005");
            employee.SetManager(lineManager);
            lineManager.SetManager(employee);
            await db.SaveChangesAsync();

            var reports = await db.ReportIds(lineManager.Id).ToListAsync();

            Assert.Contains(employee.Id, reports);
            Assert.DoesNotContain(lineManager.Id, reports);
            await transaction.RollbackAsync();
            return 0;
        });
    }

    /// <summary>Runs the scope filter directly for scopes no endpoint uses for employee records yet.</summary>
    private Task<List<string>> QueryAsAsync(string devUser, DataScope[] scopes) =>
        factory.WithDbAsync(async db =>
        {
            var user = DevUsers.Find(devUser)!;
            var self = await db.Employees.SingleAsync(e => e.UserObjectId == devUser);
            var access = new UserAccess(user.Roles.ToHashSet(), self.Id, self.DepartmentId, self.CountryCode, null);

            return await db.Employees.WithinScopes(db, access, scopes.ToHashSet())
                .Where(e => e.EmployeeNumber.Length == 7)
                .Select(e => e.EmployeeNumber)
                .OrderBy(n => n)
                .ToListAsync();
        });
}
