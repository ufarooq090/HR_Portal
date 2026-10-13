using System.Net;
using System.Net.Http.Json;
using HrPortal.Api.Employees;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Security;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Auth.HrAccessEndpoints;

namespace HrPortal.IntegrationTests.Auth;

/// <summary>HR Admins managing other HR users' country and compensation access (MC-03).</summary>
[Collection(ApiCollection.Name)]
public class HrAccessEndpointTests(HrPortalApiFactory factory)
{
    private static readonly UpdateHrAccessRequest PakistanAndUs = new(false, ["PK", "US"], false);

    [Theory]
    [MemberData(nameof(SeedData.DevUsersExceptHrAdmins), MemberType = typeof(SeedData))]
    public async Task NFR_02_only_HR_admins_can_manage_HR_access(string devUser)
    {
        var client = factory.CreateClientAs(devUser);
        var target = await factory.EmployeeIdAsync("PK-0002");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/access/hr")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/access/hr/{target}", PakistanAndUs)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/access/hr/{target}")).StatusCode);
    }

    [Fact]
    public async Task MC_03_HR_admin_grants_a_country_and_the_change_is_audited()
    {
        var admin = factory.CreateClientAs("dev-us-hradmin");
        var executive = await factory.EmployeeIdAsync("PK-0002");

        try
        {
            var response = await admin.PutAsJsonAsync($"/api/access/hr/{executive}", new UpdateHrAccessRequest(false, ["us", "PK"], true));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var saved = (await response.Content.ReadFromJsonAsync<HrAccessResponse>())!;
            Assert.Equal(["PK", "US"], saved.CountryCodes);
            Assert.True(saved.CompensationAccess);
            Assert.Contains((await admin.GetFromJsonAsync<List<HrAccessResponse>>("/api/access/hr"))!, r => r.EmployeeId == executive);

            var visible = await factory.CreateClientAs("dev-pk-hrexecutive").GetFromJsonAsync<List<EmployeeEndpoints.EmployeeSummary>>("/api/employees");
            Assert.Contains(visible!, e => e.CountryCode == "US");

            Assert.True(await factory.WithDbAsync(db => db.AuditEntries.AnyAsync(a =>
                a.EntityType == "HrAccessSettings" && a.EntityId == executive.ToString() && a.ActorId == "dev-us-hradmin")));

            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/access/hr/{executive}")).StatusCode);
            Assert.True(await factory.WithDbAsync(db => db.AuditEntries.AnyAsync(a =>
                a.EntityType == "HrAccessSettings" && a.EntityId == executive.ToString() && a.Action == AuditAction.Deleted)));
        }
        finally
        {
            await factory.ResetHrAccessAsync("dev-pk-hrexecutive");
        }
    }

    [Fact]
    public async Task MC_03_HR_admin_cannot_change_their_own_access()
    {
        var self = await factory.EmployeeIdAsync("PK-0001");

        var response = await factory.CreateClientAs("dev-pk-hradmin").PutAsJsonAsync($"/api/access/hr/{self}", PakistanAndUs);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MC_03_country_limited_HR_admin_cannot_grant_beyond_their_countries()
    {
        await using var _ = await factory.SetHrAccessAsync("dev-pk-hradmin", CountryScope.Of(["PK"]));
        var admin = factory.CreateClientAs("dev-pk-hradmin");
        var pkExecutive = await factory.EmployeeIdAsync("PK-0002");
        var usExecutive = await factory.EmployeeIdAsync("US-0002");

        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/access/hr/{pkExecutive}", PakistanAndUs)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/access/hr/{pkExecutive}", new UpdateHrAccessRequest(true, null, false))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/access/hr/{usExecutive}", new UpdateHrAccessRequest(false, ["PK"], false))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/access/hr/{pkExecutive}", new UpdateHrAccessRequest(false, ["PK"], true))).StatusCode);

            // Resetting to the role default could widen access, so only an all-country admin may do it.
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/access/hr/{pkExecutive}")).StatusCode);
        }
        finally
        {
            await factory.ResetHrAccessAsync("dev-pk-hrexecutive");
        }
    }

    [Fact]
    public async Task HR_access_list_hides_users_outside_the_admins_countries()
    {
        await using var limited = await factory.SetHrAccessAsync("dev-pk-hradmin", CountryScope.Of(["PK"]));
        await using var usExecutive = await factory.SetHrAccessAsync("dev-us-hrexecutive", CountryScope.Of(["US"]), compensation: true);

        var rows = await factory.CreateClientAs("dev-pk-hradmin").GetFromJsonAsync<List<HrAccessResponse>>("/api/access/hr");

        Assert.All(rows!, r => Assert.Equal("PK", r.EmployeeCountryCode));
    }

    [Fact]
    public async Task Invalid_requests_return_400_or_404()
    {
        var admin = factory.CreateClientAs("dev-us-hradmin");
        var executive = await factory.EmployeeIdAsync("PK-0002");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/access/hr/{executive}", new UpdateHrAccessRequest(false, ["GB"], false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/access/hr/{executive}", new UpdateHrAccessRequest(false, [], false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/access/hr/{Guid.NewGuid()}", PakistanAndUs)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/access/hr/{executive}")).StatusCode);
    }
}
