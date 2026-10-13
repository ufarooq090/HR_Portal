using System.Net;
using System.Net.Http.Json;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Security;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Employees.CompensationEndpoints;
using static HrPortal.Api.Employees.EmployeeEndpoints;

namespace HrPortal.IntegrationTests.Employees;

/// <summary>EMP-03, MC-02, NFR-03, NFR-04: who sees salary, encryption at rest, audited views.</summary>
[Collection(ApiCollection.Name)]
public class CompensationTests(HrPortalApiFactory factory)
{
    /// <summary>
    /// Reading the seeded PK employee's salary: HR Admins (all countries by default) and the employee themselves.
    /// Not HR Executives without a grant, not their line or hiring manager, not System Admins.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeedData.DevUsersData), MemberType = typeof(SeedData))]
    public async Task EMP_03_only_permitted_roles_see_an_employees_salary(string devUser)
    {
        var employee = await factory.EmployeeIdAsync("PK-0006");

        var response = await factory.CreateClientAs(devUser).GetAsync($"/api/employees/{employee}/compensation");

        var allowed = devUser is "dev-pk-hradmin" or "dev-us-hradmin" or "dev-pk-employee";
        Assert.True((allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden) == response.StatusCode, $"{devUser}: {response.StatusCode}");
    }

    [Fact]
    public async Task EMP_03_salary_is_never_part_of_the_employee_record_response()
    {
        var detail = await factory.CreateClientAs("dev-pk-hradmin").GetStringAsync($"/api/employees/{await factory.EmployeeIdAsync("PK-0006")}");

        Assert.DoesNotContain("annualCtc", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("3600000", detail);
    }

    [Fact]
    public async Task EMP_03_HR_executive_sees_salary_once_granted()
    {
        var employee = await factory.EmployeeIdAsync("PK-0006");
        var client = factory.CreateClientAs("dev-pk-hrexecutive");

        await using (await factory.SetHrAccessAsync("dev-pk-hrexecutive", CountryScope.Of(["PK"]), compensation: true))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/employees/{employee}/compensation")).StatusCode);
            var detail = await client.GetFromJsonAsync<EmployeeDetail>($"/api/employees/{employee}");
            Assert.True(detail!.Permissions.CanViewCompensation);
            Assert.True(detail.Permissions.CanEditCompensation);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/employees/{employee}/compensation")).StatusCode);
    }

    [Fact]
    public async Task NFR_04_every_salary_read_is_audited()
    {
        var employee = await factory.EmployeeIdAsync("PK-0006");
        Task<int> CountAsync() => factory.WithDbAsync(db => db.AuditEntries.CountAsync(a =>
            a.Action == AuditAction.Viewed && a.EntityType == "Compensation" && a.EntityId == employee.ToString() && a.ActorId == "dev-us-hradmin"));
        var before = await CountAsync();

        await factory.CreateClientAs("dev-us-hradmin").GetAsync($"/api/employees/{employee}/compensation");

        Assert.Equal(before + 1, await CountAsync());
    }

    [Fact]
    public async Task EMP_05_increments_build_a_salary_history_and_the_latest_in_effect_is_current()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        var client = factory.CreateClientAs("dev-pk-hradmin");

        await client.PostAsJsonAsync($"/api/employees/{employee.Id}/compensation", new AddCompensationRequest(employee.JoiningDate, 1_200_000m, "PKR", "B1", CompensationReason.Joining, null));
        await client.PostAsJsonAsync($"/api/employees/{employee.Id}/compensation", new AddCompensationRequest(new DateOnly(2025, 7, 1), 1_440_000m, "pkr", "B1", CompensationReason.Increment, "20% increment"));
        var response = await client.PostAsJsonAsync($"/api/employees/{employee.Id}/compensation", new AddCompensationRequest(new DateOnly(2099, 1, 1), 2_000_000m, "PKR", "B2", CompensationReason.Promotion, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = (await response.Content.ReadFromJsonAsync<List<CompensationDto>>())!;
        Assert.Equal([2_000_000m, 1_440_000m, 1_200_000m], history.Select(h => h.AnnualCtc));
        Assert.Equal(1_440_000m, history.Single(h => h.IsCurrent).AnnualCtc);
        Assert.All(history, h => Assert.Equal("PKR", h.CurrencyCode));
    }

    [Fact]
    public async Task MC_02_salary_must_be_in_the_local_currency()
    {
        var employee = await factory.CreateEmployeeAsync("PK");

        var response = await factory.CreateClientAs("dev-pk-hradmin").PostAsJsonAsync(
            $"/api/employees/{employee.Id}/compensation", new AddCompensationRequest(employee.JoiningDate, 90_000m, "USD", null, CompensationReason.Joining, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("dev-pk-hrexecutive")]
    [InlineData("dev-pk-linemanager")]
    [InlineData("dev-pk-employee")]
    [InlineData("dev-pk-systemadmin")]
    public async Task EMP_03_only_HR_admins_and_granted_executives_record_salary(string devUser)
    {
        var employee = await factory.CreateEmployeeAsync("PK", await factory.EmployeeIdAsync("PK-0005"));

        var response = await factory.CreateClientAs(devUser).PostAsJsonAsync(
            $"/api/employees/{employee.Id}/compensation", new AddCompensationRequest(employee.JoiningDate, 1m, "PKR", null, CompensationReason.Joining, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NFR_03_salary_amount_band_and_note_are_encrypted_at_rest_and_masked_in_audit()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        await factory.CreateClientAs("dev-pk-hradmin").PostAsJsonAsync(
            $"/api/employees/{employee.Id}/compensation", new AddCompensationRequest(employee.JoiningDate, 7_654_321m, "PKR", "Band Secret", CompensationReason.Joining, "Matched offer 7654321"));

        var raw = await factory.WithDbAsync(db => db.Database
            .SqlQuery<string>($"""SELECT concat_ws('|', "AnnualCtcAmount", "SalaryBand", "Note") AS "Value" FROM "Compensation" WHERE "EmployeeId" = {employee.Id}""")
            .SingleAsync());
        var audit = await factory.WithDbAsync(db => db.AuditEntries
            .Where(a => a.EntityType == "Compensation" && a.Action == AuditAction.Created && a.ActorId == "dev-pk-hradmin")
            .OrderByDescending(a => a.Id).Select(a => a.NewValues).FirstAsync());

        Assert.DoesNotContain("7654321", raw);
        Assert.DoesNotContain("Secret", raw);
        Assert.DoesNotContain("7654321", audit);
        Assert.Contains("PKR", audit);
    }
}
