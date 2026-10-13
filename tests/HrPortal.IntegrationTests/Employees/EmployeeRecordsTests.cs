using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Security;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Employees.EmployeeEndpoints;

namespace HrPortal.IntegrationTests.Employees;

/// <summary>EMP-01, 02, 04, 05, 08 with data scoping (NFR-02, MC-03).</summary>
[Collection(ApiCollection.Name)]
public class EmployeeRecordsTests(HrPortalApiFactory factory)
{
    private static readonly PersonalDetailsDto Personal = new(
        "ayesha.personal@mail.test", "+92 300 1112223", new DateOnly(1994, 3, 9), "12 Canal Road", null, "Lahore", "Punjab",
        "54000", "PK", "35202-1234567-1", [new EmergencyContactDto("Bilal Khan", "Brother", "+92 300 9998887")]);

    [Fact]
    public async Task EMP_02_HR_executive_adds_an_employee_in_their_country()
    {
        var request = await factory.NewRequestAsync("PK", personal: Personal);

        var response = await factory.CreateClientAs("dev-pk-hrexecutive").PostAsJsonAsync("/api/employees", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<EmployeeDetail>())!;
        Assert.Equal(request.EmployeeNumber, created.EmployeeNumber);
        Assert.Equal("PK", created.CountryCode);
        Assert.Equal(EmploymentStatus.Probation, created.Status);
        Assert.Equal(request.JoiningDate.AddMonths(3), created.ProbationEndDate);
        Assert.Equal("35202-1234567-1", created.Personal!.NationalId);
        Assert.Equal("Bilal Khan", Assert.Single(created.Personal.EmergencyContacts).Name);
        Assert.True(created.Permissions.CanEdit);
        Assert.False(created.Permissions.CanViewCompensation);
        Assert.False(created.Permissions.CanTerminate);
    }

    [Fact]
    public async Task MC_03_HR_executive_cannot_add_an_employee_in_another_country()
    {
        var response = await factory.CreateClientAs("dev-pk-hrexecutive").PostAsJsonAsync("/api/employees", await factory.NewRequestAsync("US"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(NonHrUsers))]
    public async Task NFR_02_only_HR_can_add_employees(string devUser)
    {
        var response = await factory.CreateClientAs(devUser).PostAsJsonAsync("/api/employees", await factory.NewRequestAsync("PK"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public static TheoryData<string> NonHrUsers => new(SeedData.AllDevUsers.Where(u => !u.EndsWith("hradmin", StringComparison.Ordinal) && !u.EndsWith("hrexecutive", StringComparison.Ordinal)));

    [Fact]
    public async Task Duplicate_employee_number_returns_409_and_invalid_input_400()
    {
        var client = factory.CreateClientAs("dev-pk-hradmin");
        var duplicate = await factory.NewRequestAsync("PK") with { EmployeeNumber = "PK-0001" };
        var badEmail = await factory.NewRequestAsync("PK") with { WorkEmail = "not-an-email" };

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/employees", duplicate)).StatusCode);
        var bad = await client.PostAsJsonAsync("/api/employees", badEmail);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.True((await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("workEmail", out _));
    }

    [Fact]
    public async Task EMP_01_personal_details_are_shown_to_HR_and_the_employee_but_not_to_managers()
    {
        var lineManagerId = await factory.EmployeeIdAsync("PK-0005");
        var employee = await factory.CreateEmployeeAsync("PK", lineManagerId, Personal);

        var asManager = await factory.CreateClientAs("dev-pk-linemanager").GetFromJsonAsync<EmployeeDetail>($"/api/employees/{employee.Id}");
        var asHiringManager = await factory.CreateClientAs("dev-pk-hiringmanager").GetFromJsonAsync<EmployeeDetail>($"/api/employees/{employee.Id}");
        var asHr = await factory.CreateClientAs("dev-pk-hrexecutive").GetFromJsonAsync<EmployeeDetail>($"/api/employees/{employee.Id}");
        var self = await factory.CreateClientAs("dev-pk-employee").GetFromJsonAsync<EmployeeDetail>($"/api/employees/{await factory.EmployeeIdAsync("PK-0006")}");

        Assert.Null(asManager!.Personal);
        Assert.False(asManager.Permissions.CanViewPersonal);
        Assert.False(asManager.Permissions.CanEdit);
        Assert.Null(asHiringManager!.Personal);
        Assert.Equal("35202-1234567-1", asHr!.Personal!.NationalId);
        Assert.True(asHr.Permissions.CanEditPersonal);
        Assert.NotNull(self!.Personal);
        Assert.False(self.Permissions.CanEdit);
        Assert.False(self.Permissions.CanEditPersonal);
        Assert.True(self.Permissions.CanViewCompensation);
    }

    [Fact]
    public async Task NFR_03_national_id_is_encrypted_at_rest_and_masked_in_the_audit_trail()
    {
        var employee = await factory.CreateEmployeeAsync("PK", personal: Personal);

        var stored = await factory.WithDbAsync(db => db.Database
            .SqlQuery<string>($"""SELECT p."NationalId" AS "Value" FROM "People" p JOIN "Employees" e ON e."PersonId" = p."Id" WHERE e."Id" = {employee.Id}""")
            .SingleAsync());
        var personId = await factory.WithDbAsync(db => db.Employees.Where(e => e.Id == employee.Id).Select(e => e.PersonId.ToString()).SingleAsync());
        var audit = await factory.WithDbAsync(db => db.AuditEntries
            .Where(a => a.EntityType == "Person" && a.Action == AuditAction.Created && a.EntityId == personId)
            .SingleAsync());

        Assert.DoesNotContain("35202", stored);
        Assert.DoesNotContain("35202", audit.NewValues);
        Assert.Equal("***", JsonDocument.Parse(audit.NewValues!).RootElement.GetProperty("NationalId").GetString());
    }

    [Fact]
    public async Task EMP_01_HR_updates_personal_details_but_employees_and_managers_cannot()
    {
        var employee = await factory.CreateEmployeeAsync("PK", await factory.EmployeeIdAsync("PK-0005"), Personal);
        var update = new UpdatePersonalRequest("Ayesha", "Raza", Personal with { City = "Karachi", EmergencyContacts = [] });

        var asHr = await factory.CreateClientAs("dev-pk-hradmin").PutAsJsonAsync($"/api/employees/{employee.Id}/personal", update);
        var asManager = await factory.CreateClientAs("dev-pk-linemanager").PutAsJsonAsync($"/api/employees/{employee.Id}/personal", update);
        var self = await factory.CreateClientAs("dev-pk-employee").PutAsJsonAsync($"/api/employees/{await factory.EmployeeIdAsync("PK-0006")}/personal", update);

        Assert.Equal(HttpStatusCode.OK, asHr.StatusCode);
        var updated = (await asHr.Content.ReadFromJsonAsync<EmployeeDetail>())!;
        Assert.Equal("Raza", updated.FamilyName);
        Assert.Equal("Karachi", updated.Personal!.City);
        Assert.Empty(updated.Personal.EmergencyContacts);
        Assert.Equal(HttpStatusCode.Forbidden, asManager.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
    }

    [Fact]
    public async Task EMP_05_promotion_and_transfer_appear_in_dated_history()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        var client = factory.CreateClientAs("dev-pk-hradmin");
        var finance = await factory.WithDbAsync(db => db.Departments.Where(d => d.Code == "FIN").Select(d => d.Id).SingleAsync());

        var promoted = await client.PostAsJsonAsync($"/api/employees/{employee.Id}/job-changes", new JobChangeRequest(
            JobChangeKind.Promotion, new DateOnly(2025, 7, 1), "Senior Test Engineer", employee.LegalEntityId, employee.OfficeLocationId, employee.DepartmentId, employee.GradeId, "Mid-year review"));
        var transferred = await client.PostAsJsonAsync($"/api/employees/{employee.Id}/job-changes", new JobChangeRequest(
            JobChangeKind.Transfer, new DateOnly(2025, 9, 1), "Senior Test Engineer", employee.LegalEntityId, employee.OfficeLocationId, finance, employee.GradeId, null));

        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, transferred.StatusCode);
        var history = await client.GetFromJsonAsync<List<HistoryEntryDto>>($"/api/employees/{employee.Id}/history");
        Assert.Equal([JobChangeKind.Transfer, JobChangeKind.Promotion, JobChangeKind.Joined], history!.Select(h => h.Kind));
        Assert.Equal("Finance", history![0].DepartmentName);
        Assert.Equal("Engineering", history[1].DepartmentName);
        Assert.Equal("Senior Test Engineer", history[1].Designation);
        Assert.Equal("Mid-year review", history[1].Reason);
    }

    [Fact]
    public async Task MC_03_country_limited_HR_cannot_transfer_someone_to_another_country()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        var us = await factory.WithDbAsync(async db => new
        {
            Entity = await db.LegalEntities.Where(e => e.CountryCode == "US" && e.Name.StartsWith("HR Portal Dev")).Select(e => e.Id).FirstAsync(),
            Office = await db.OfficeLocations.Where(e => e.CountryCode == "US" && e.Name == "US Office").Select(e => e.Id).SingleAsync(),
        });

        var response = await factory.CreateClientAs("dev-pk-hrexecutive").PostAsJsonAsync($"/api/employees/{employee.Id}/job-changes", new JobChangeRequest(
            JobChangeKind.Transfer, new DateOnly(2025, 9, 1), "Test Engineer", us.Entity, us.Office, employee.DepartmentId, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EMP_05_manager_change_is_recorded_and_a_reporting_loop_is_refused()
    {
        var manager = await factory.CreateEmployeeAsync("PK");
        var report = await factory.CreateEmployeeAsync("PK", manager.Id);
        var client = factory.CreateClientAs("dev-pk-hradmin");

        var loop = await client.PostAsJsonAsync($"/api/employees/{manager.Id}/manager", new ManagerChangeRequest(report.Id, new DateOnly(2025, 6, 1), null));
        var moved = await client.PostAsJsonAsync($"/api/employees/{report.Id}/manager", new ManagerChangeRequest(null, new DateOnly(2025, 6, 1), "Reorg"));

        Assert.Equal(HttpStatusCode.BadRequest, loop.StatusCode);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Null((await moved.Content.ReadFromJsonAsync<EmployeeDetail>())!.ManagerId);
        var history = await client.GetFromJsonAsync<List<HistoryEntryDto>>($"/api/employees/{report.Id}/history");
        Assert.Equal(JobChangeKind.ManagerChange, history![0].Kind);
        Assert.Equal(manager.GivenName + " " + manager.FamilyName, history[1].ManagerName);
    }

    [Fact]
    public async Task EMP_04_only_HR_admins_record_terminations()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        var request = new StatusChangeRequest(EmploymentStatus.Terminated, new DateOnly(2025, 8, 1), null, "Misconduct");

        var asExecutive = await factory.CreateClientAs("dev-pk-hrexecutive").PostAsJsonAsync($"/api/employees/{employee.Id}/status", request);
        var asAdmin = await factory.CreateClientAs("dev-pk-hradmin").PostAsJsonAsync($"/api/employees/{employee.Id}/status", request);

        Assert.Equal(HttpStatusCode.Forbidden, asExecutive.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
        var terminated = (await asAdmin.Content.ReadFromJsonAsync<EmployeeDetail>())!;
        Assert.Equal(EmploymentStatus.Terminated, terminated.Status);
        Assert.Equal(new DateOnly(2025, 8, 1), terminated.LastWorkingDate);
        Assert.Empty(terminated.NextStatuses);
    }

    [Fact]
    public async Task EMP_04_resignation_runs_through_notice_and_ends_job_changes()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        var client = factory.CreateClientAs("dev-pk-hrexecutive");

        var notice = await client.PostAsJsonAsync($"/api/employees/{employee.Id}/status", new StatusChangeRequest(EmploymentStatus.OnNotice, new DateOnly(2025, 9, 1), null, null));
        var resigned = await client.PostAsJsonAsync($"/api/employees/{employee.Id}/status", new StatusChangeRequest(EmploymentStatus.Resigned, new DateOnly(2025, 10, 1), null, null));
        var promotion = await client.PostAsJsonAsync($"/api/employees/{employee.Id}/job-changes", new JobChangeRequest(
            JobChangeKind.Promotion, new DateOnly(2025, 10, 2), "Lead", employee.LegalEntityId, employee.OfficeLocationId, employee.DepartmentId, null, null));
        var back = await client.PostAsJsonAsync($"/api/employees/{employee.Id}/status", new StatusChangeRequest(EmploymentStatus.Confirmed, new DateOnly(2025, 10, 2), null, null));

        Assert.Equal(new DateOnly(2025, 10, 1), (await notice.Content.ReadFromJsonAsync<EmployeeDetail>())!.LastWorkingDate);
        Assert.Equal(HttpStatusCode.OK, resigned.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, promotion.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, back.StatusCode);
    }

    [Theory]
    [InlineData("dev-pk-linemanager")]
    [InlineData("dev-pk-employee")]
    [InlineData("dev-pk-systemadmin")]
    [InlineData("dev-us-hrexecutive")]
    public async Task NFR_02_users_without_edit_scope_cannot_change_jobs_or_status(string devUser)
    {
        var employee = await factory.CreateEmployeeAsync("PK", await factory.EmployeeIdAsync("PK-0005"));
        var client = factory.CreateClientAs(devUser);

        var status = await client.PostAsJsonAsync($"/api/employees/{employee.Id}/status", new StatusChangeRequest(EmploymentStatus.Confirmed, new DateOnly(2025, 6, 1), null, null));
        var manager = await client.PostAsJsonAsync($"/api/employees/{employee.Id}/manager", new ManagerChangeRequest(null, new DateOnly(2025, 6, 1), null));

        Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, manager.StatusCode);
    }

    [Theory]
    [MemberData(nameof(SeedData.DevUsersData), MemberType = typeof(SeedData))]
    public async Task EMP_05_history_follows_the_directory_scope(string devUser)
    {
        var client = factory.CreateClientAs(devUser);
        var finance = await factory.EmployeeIdAsync("PK-0009");

        var response = await client.GetAsync($"/api/employees/{finance}/history");

        var expected = SeedData.VisibleTo(devUser).Contains("PK-0009") ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task EMP_08_directory_filters_by_department_office_manager_status_and_country()
    {
        var client = factory.CreateClientAs("dev-pk-hradmin");
        var finance = await factory.WithDbAsync(db => db.Departments.Where(d => d.Code == "FIN").Select(d => d.Id).SingleAsync());
        var hiringManager = await factory.EmployeeIdAsync("PK-0004");

        var byDepartment = await client.GetFromJsonAsync<List<EmployeeSummary>>($"/api/employees?departmentId={finance}&countryCode=PK");
        var byManager = await client.GetFromJsonAsync<List<EmployeeSummary>>($"/api/employees?managerId={hiringManager}");
        var onProbation = await client.GetFromJsonAsync<List<EmployeeSummary>>("/api/employees?status=Probation&countryCode=us");

        Assert.Equal(["PK-0009"], byDepartment!.Select(e => e.EmployeeNumber).Where(SeedData.IsSeeded));
        Assert.Equal(["PK-0005", "PK-0007"], byManager!.Select(e => e.EmployeeNumber).Where(SeedData.IsSeeded).Order());
        Assert.Equal(["US-0008"], onProbation!.Select(e => e.EmployeeNumber).Where(SeedData.IsSeeded));
        Assert.All(onProbation!, e => Assert.Equal(EmploymentStatus.Probation, e.Status));
    }

    [Fact]
    public async Task EMP_08_filters_never_widen_the_scope()
    {
        var finance = await factory.WithDbAsync(db => db.Departments.Where(d => d.Code == "FIN").Select(d => d.Id).SingleAsync());

        var results = await factory.CreateClientAs("dev-pk-linemanager").GetFromJsonAsync<List<EmployeeSummary>>($"/api/employees?departmentId={finance}");

        Assert.Empty(results!);
    }

    [Fact]
    public async Task Changes_are_audited_with_the_actor()
    {
        var employee = await factory.CreateEmployeeAsync("PK", asUser: "dev-pk-hrexecutive");

        var actions = await factory.WithDbAsync(db => db.AuditEntries
            .Where(a => a.EntityId == employee.Id.ToString() && a.EntityType == "Employee")
            .Select(a => a.ActorId)
            .ToListAsync());

        Assert.Equal(["dev-pk-hrexecutive"], actions);
    }

    [Fact]
    public async Task MC_03_country_limited_HR_admin_cannot_read_or_edit_other_countries()
    {
        await using var _ = await factory.SetHrAccessAsync("dev-pk-hradmin", CountryScope.Of(["PK"]));
        var usEmployee = await factory.CreateEmployeeAsync("US");
        var client = factory.CreateClientAs("dev-pk-hradmin");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/employees/{usEmployee.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/employees/{usEmployee.Id}/status",
            new StatusChangeRequest(EmploymentStatus.Terminated, new DateOnly(2025, 6, 1), null, null))).StatusCode);
    }
}
