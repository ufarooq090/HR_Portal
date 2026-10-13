using System.Net;
using System.Net.Http.Json;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Employees;
using HrPortal.Infrastructure.Employees;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HrPortal.Api.Employees.EmployeeEndpoints;

namespace HrPortal.IntegrationTests.Employees;

/// <summary>Future-dated job and manager changes (HR decision 13 Oct 2026): scheduled, editable, applied by a background job.</summary>
[Collection(ApiCollection.Name)]
public class ScheduledChangeTests(HrPortalApiFactory factory)
{
    private static readonly TimeZoneInfo Karachi = TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>A UTC instant at a given local wall-clock time.</summary>
    private static DateTimeOffset At(DateOnly date, int hour, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(new TimeOnly(hour, 0));
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static JobChangeRequest Promotion(EmployeeDetail e, DateOnly effective, string designation = "Lead Test Engineer") =>
        new(JobChangeKind.Promotion, effective, designation, e.LegalEntityId, e.OfficeLocationId, e.DepartmentId, e.GradeId, "Planned");

    private async Task<int> RunJobAsync(DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ScheduledChangeRunner>().ApplyDueAsync(now, CancellationToken.None);
    }

    private Task<ScheduledJobChange> ChangeAsync(Guid id) => factory.WithDbAsync(db => db.ScheduledJobChanges.AsNoTracking().SingleAsync(c => c.Id == id));

    [Fact]
    public async Task Future_promotion_is_scheduled_as_upcoming_and_does_not_change_the_record_yet()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        var client = factory.CreateClientAs("dev-pk-hrexecutive");

        var response = await client.PostAsJsonAsync($"/api/employees/{employee.Id}/job-changes", Promotion(employee, new DateOnly(2027, 1, 15)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<EmployeeDetail>())!;
        Assert.Equal("Test Engineer", detail.Designation);
        var upcoming = Assert.Single(detail.Upcoming);
        Assert.Equal(ScheduledChangeStatus.Pending, upcoming.Status);
        Assert.Equal(new DateOnly(2027, 1, 15), upcoming.EffectiveDate);
        Assert.Equal("Lead Test Engineer", upcoming.Designation);
        Assert.Equal("Engineering", upcoming.DepartmentName);
        var history = await client.GetFromJsonAsync<List<HistoryEntryDto>>($"/api/employees/{employee.Id}/history");
        Assert.Equal([JobChangeKind.Joined], history!.Select(h => h.Kind));
    }

    [Fact]
    public async Task HR_edits_and_cancels_a_pending_change()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        var client = factory.CreateClientAs("dev-pk-hradmin");
        var finance = await factory.WithDbAsync(db => db.Departments.Where(d => d.Code == "FIN").Select(d => d.Id).SingleAsync());
        var scheduled = (await (await client.PostAsJsonAsync($"/api/employees/{employee.Id}/job-changes", Promotion(employee, new DateOnly(2027, 2, 1))))
            .Content.ReadFromJsonAsync<EmployeeDetail>())!.Upcoming.Single();

        var edited = await client.PutAsJsonAsync($"/api/employees/{employee.Id}/scheduled-changes/{scheduled.Id}", new UpdateScheduledChangeRequest(
            new DateOnly(2027, 3, 1), "Moved to Finance", "Finance Lead", employee.LegalEntityId, employee.OfficeLocationId, finance, null, null));

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var upcoming = (await edited.Content.ReadFromJsonAsync<EmployeeDetail>())!.Upcoming.Single();
        Assert.Equal((new DateOnly(2027, 3, 1), "Finance Lead", "Finance", "Moved to Finance"), (upcoming.EffectiveDate, upcoming.Designation, upcoming.DepartmentName, upcoming.Reason));

        var cancelled = await client.DeleteAsync($"/api/employees/{employee.Id}/scheduled-changes/{scheduled.Id}");

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Empty((await cancelled.Content.ReadFromJsonAsync<EmployeeDetail>())!.Upcoming);
        Assert.Equal(ScheduledChangeStatus.Cancelled, (await ChangeAsync(scheduled.Id)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/employees/{employee.Id}/scheduled-changes/{scheduled.Id}")).StatusCode);
        Assert.True(await factory.WithDbAsync(db => db.AuditEntries.AnyAsync(a =>
            a.EntityType == "ScheduledJobChange" && a.EntityId == scheduled.Id.ToString() && a.Action == AuditAction.Updated && a.ActorId == "dev-pk-hradmin")));
    }

    [Fact]
    public async Task Job_applies_the_change_on_its_effective_date_and_records_history_as_the_system()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        var effective = new DateOnly(2027, 4, 5);
        var change = (await (await factory.CreateClientAs("dev-pk-hradmin").PostAsJsonAsync($"/api/employees/{employee.Id}/job-changes", Promotion(employee, effective)))
            .Content.ReadFromJsonAsync<EmployeeDetail>())!.Upcoming.Single();

        await RunJobAsync(At(effective.AddDays(-1), 23, Karachi));
        Assert.Equal(ScheduledChangeStatus.Pending, (await ChangeAsync(change.Id)).Status);

        await RunJobAsync(At(effective, 0, Karachi).AddMinutes(5));

        Assert.Equal(ScheduledChangeStatus.Applied, (await ChangeAsync(change.Id)).Status);
        var detail = await factory.CreateClientAs("dev-pk-hradmin").GetFromJsonAsync<EmployeeDetail>($"/api/employees/{employee.Id}");
        Assert.Equal("Lead Test Engineer", detail!.Designation);
        Assert.Empty(detail.Upcoming);
        var history = await factory.CreateClientAs("dev-pk-hradmin").GetFromJsonAsync<List<HistoryEntryDto>>($"/api/employees/{employee.Id}/history");
        Assert.Equal((JobChangeKind.Promotion, effective, "Planned"), (history![0].Kind, history[0].EffectiveDate, history[0].Reason));
        Assert.True(await factory.WithDbAsync(db => db.AuditEntries.AnyAsync(a =>
            a.EntityType == "Employee" && a.EntityId == employee.Id.ToString() && a.Action == AuditAction.Updated && a.ActorId == "system")));
    }

    [Fact]
    public async Task Changes_take_effect_at_local_midnight_in_each_office()
    {
        var pk = await factory.CreateEmployeeAsync("PK");
        var us = await factory.CreateEmployeeAsync("US");
        var effective = new DateOnly(2027, 5, 10);
        var pkChange = (await (await factory.CreateClientAs("dev-pk-hradmin").PostAsJsonAsync($"/api/employees/{pk.Id}/job-changes", Promotion(pk, effective)))
            .Content.ReadFromJsonAsync<EmployeeDetail>())!.Upcoming.Single();
        var usChange = (await (await factory.CreateClientAs("dev-us-hradmin").PostAsJsonAsync($"/api/employees/{us.Id}/job-changes", Promotion(us, effective)))
            .Content.ReadFromJsonAsync<EmployeeDetail>())!.Upcoming.Single();

        // 01:00 in Karachi is still the previous afternoon in New York.
        await RunJobAsync(At(effective, 1, Karachi));
        Assert.Equal(ScheduledChangeStatus.Applied, (await ChangeAsync(pkChange.Id)).Status);
        Assert.Equal(ScheduledChangeStatus.Pending, (await ChangeAsync(usChange.Id)).Status);

        await RunJobAsync(At(effective, 1, NewYork));
        Assert.Equal(ScheduledChangeStatus.Applied, (await ChangeAsync(usChange.Id)).Status);
    }

    [Fact]
    public async Task Reporting_loop_is_refused_when_scheduling()
    {
        var manager = await factory.CreateEmployeeAsync("PK");
        var report = await factory.CreateEmployeeAsync("PK", manager.Id);

        var response = await factory.CreateClientAs("dev-pk-hradmin").PostAsJsonAsync(
            $"/api/employees/{manager.Id}/manager", new ManagerChangeRequest(report.Id, new DateOnly(2027, 6, 1), null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reporting_loop_that_appears_after_scheduling_fails_the_change_when_applied()
    {
        var a = await factory.CreateEmployeeAsync("PK");
        var b = await factory.CreateEmployeeAsync("PK");
        var client = factory.CreateClientAs("dev-pk-hradmin");
        var effective = new DateOnly(2027, 7, 1);

        // Scheduled while valid: A will report to B.
        var change = (await (await client.PostAsJsonAsync($"/api/employees/{a.Id}/manager", new ManagerChangeRequest(b.Id, effective, null)))
            .Content.ReadFromJsonAsync<EmployeeDetail>())!.Upcoming.Single();
        Assert.Equal(b.GivenName + " " + b.FamilyName, change.NewManagerName);

        // Meanwhile B is made to report to A, so applying would create a loop.
        await client.PostAsJsonAsync($"/api/employees/{b.Id}/manager", new ManagerChangeRequest(a.Id, new DateOnly(2025, 6, 1), null));
        await RunJobAsync(At(effective, 9, Karachi));

        var failed = await ChangeAsync(change.Id);
        Assert.Equal(ScheduledChangeStatus.Failed, failed.Status);
        Assert.Contains("loop", failed.Outcome);
        var detail = await client.GetFromJsonAsync<EmployeeDetail>($"/api/employees/{a.Id}");
        Assert.Null(detail!.ManagerId);
        Assert.Equal(ScheduledChangeStatus.Failed, Assert.Single(detail.Upcoming).Status);
    }

    [Fact]
    public async Task Leaving_cancels_pending_changes()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        var client = factory.CreateClientAs("dev-pk-hradmin");
        var change = (await (await client.PostAsJsonAsync($"/api/employees/{employee.Id}/job-changes", Promotion(employee, new DateOnly(2027, 8, 1))))
            .Content.ReadFromJsonAsync<EmployeeDetail>())!.Upcoming.Single();

        await client.PostAsJsonAsync($"/api/employees/{employee.Id}/status", new StatusChangeRequest(EmploymentStatus.Resigned, new DateOnly(2026, 1, 31), null, null));

        var cancelled = await ChangeAsync(change.Id);
        Assert.Equal(ScheduledChangeStatus.Cancelled, cancelled.Status);
        Assert.Contains("resigned", cancelled.Outcome);
    }

    [Fact]
    public async Task Past_or_current_dates_still_apply_immediately()
    {
        var employee = await factory.CreateEmployeeAsync("PK");

        var response = await factory.CreateClientAs("dev-pk-hradmin").PostAsJsonAsync(
            $"/api/employees/{employee.Id}/job-changes", Promotion(employee, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));

        var detail = (await response.Content.ReadFromJsonAsync<EmployeeDetail>())!;
        Assert.Equal("Lead Test Engineer", detail.Designation);
        Assert.Empty(detail.Upcoming);
    }

    [Theory]
    [InlineData("dev-pk-linemanager")]
    [InlineData("dev-pk-employee")]
    [InlineData("dev-pk-systemadmin")]
    [InlineData("dev-us-hrexecutive")]
    public async Task NFR_02_only_HR_in_scope_can_schedule_edit_or_cancel(string devUser)
    {
        var employee = await factory.CreateEmployeeAsync("PK", await factory.EmployeeIdAsync("PK-0005"));
        var change = (await (await factory.CreateClientAs("dev-pk-hradmin").PostAsJsonAsync($"/api/employees/{employee.Id}/job-changes", Promotion(employee, new DateOnly(2027, 9, 1))))
            .Content.ReadFromJsonAsync<EmployeeDetail>())!.Upcoming.Single();
        var client = factory.CreateClientAs(devUser);

        var schedule = await client.PostAsJsonAsync($"/api/employees/{employee.Id}/job-changes", Promotion(employee, new DateOnly(2027, 9, 2)));
        var edit = await client.PutAsJsonAsync($"/api/employees/{employee.Id}/scheduled-changes/{change.Id}", new UpdateScheduledChangeRequest(
            new DateOnly(2027, 9, 3), null, "X", employee.LegalEntityId, employee.OfficeLocationId, employee.DepartmentId, null, null));
        var cancel = await client.DeleteAsync($"/api/employees/{employee.Id}/scheduled-changes/{change.Id}");

        Assert.All([schedule, edit, cancel], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal(ScheduledChangeStatus.Pending, (await ChangeAsync(change.Id)).Status);
    }

    [Fact]
    public async Task MC_03_scheduled_transfer_to_another_country_needs_access_to_that_country()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        var us = await factory.WithDbAsync(async db => new
        {
            Entity = await db.LegalEntities.Where(e => e.CountryCode == "US" && e.Name.StartsWith("HR Portal Dev")).Select(e => e.Id).FirstAsync(),
            Office = await db.OfficeLocations.Where(e => e.Name == "US Office").Select(e => e.Id).SingleAsync(),
        });

        var response = await factory.CreateClientAs("dev-pk-hrexecutive").PostAsJsonAsync($"/api/employees/{employee.Id}/job-changes",
            new JobChangeRequest(JobChangeKind.Transfer, new DateOnly(2027, 10, 1), "Test Engineer", us.Entity, us.Office, employee.DepartmentId, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
