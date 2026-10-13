using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Security;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Organisation.CountrySettingsEndpoints;

namespace HrPortal.IntegrationTests.Organisation;

[Collection(ApiCollection.Name)]
public class CountrySettingsEndpointTests(HrPortalApiFactory factory)
{
    private static UpdateCountrySettingsRequest Pakistan(int probation = 3, int notice = 30) =>
        new("Pakistan", "Asia/Karachi", probation, notice);

    private static UpdateCountrySettingsRequest UnitedStates(int probation = 3, int notice = 30) =>
        new("United States", "America/New_York", probation, notice);

    [Fact]
    public async Task Any_signed_in_user_can_read_country_settings()
    {
        var countries = await factory.CreateClientAs("dev-pk-employee")
            .GetFromJsonAsync<List<CountrySettingsResponse>>("/api/organisation/countries");

        Assert.NotNull(countries);
        Assert.Equal(["PK", "US"], countries.Select(c => c.CountryCode).Take(2));
        Assert.Equal(["PKR", "USD"], countries.Select(c => c.CurrencyCode).Take(2));
        Assert.Equal("Asia/Karachi", countries[0].DefaultTimeZone);
    }

    [Theory]
    [MemberData(nameof(SeedData.DevUsersData), MemberType = typeof(SeedData))]
    public async Task NFR_02_only_system_and_HR_admins_can_change_country_settings(string devUser)
    {
        var client = factory.CreateClientAs(devUser);

        var update = await client.PutAsJsonAsync("/api/organisation/countries/PK", Pakistan());
        var create = await client.PostAsJsonAsync("/api/organisation/countries",
            new CreateCountryRequest("ZZ", "Test Country", "XTS", "Etc/UTC", 3, 30));

        if (devUser.EndsWith("hradmin", StringComparison.Ordinal) || devUser.EndsWith("systemadmin", StringComparison.Ordinal))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            Assert.True(create.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, $"{create.StatusCode}");
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        }
    }

    [Fact]
    public async Task HR_admin_change_is_saved_and_audited_with_old_and_new_values()
    {
        var client = factory.CreateClientAs("dev-us-hradmin");

        var response = await client.PutAsJsonAsync("/api/organisation/countries/US", UnitedStates(6, 14));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CountrySettingsResponse>();
        Assert.Equal(6, updated!.ProbationMonths);
        Assert.Equal(14, updated.NoticePeriodDays);

        var audit = await factory.WithDbAsync(db => db.AuditEntries
            .Where(a => a.EntityType == "CountrySettings" && a.EntityId == "US" && a.Action == AuditAction.Updated)
            .OrderByDescending(a => a.Id)
            .FirstAsync());
        Assert.Equal("dev-us-hradmin", audit.ActorId);
        Assert.Equal(TimeSpan.Zero, audit.OccurredAtUtc.Offset);
        Assert.Equal(3, JsonDocument.Parse(audit.OldValues!).RootElement.GetProperty("ProbationMonths").GetInt32());
        Assert.Equal(6, JsonDocument.Parse(audit.NewValues!).RootElement.GetProperty("ProbationMonths").GetInt32());

        await client.PutAsJsonAsync("/api/organisation/countries/US", UnitedStates());
    }

    [Fact]
    public async Task MC_03_country_limited_HR_admin_cannot_change_or_add_other_countries()
    {
        await using var _ = await factory.SetHrAccessAsync("dev-pk-hradmin", CountryScope.Of(["PK"]));
        var client = factory.CreateClientAs("dev-pk-hradmin");

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/organisation/countries/PK", Pakistan())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/organisation/countries/US", UnitedStates())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/organisation/countries",
            new CreateCountryRequest("GB", "United Kingdom", "GBP", "Europe/London", 6, 30))).StatusCode);
    }

    [Fact]
    public async Task Duplicate_country_returns_409()
    {
        var response = await factory.CreateClientAs("dev-pk-systemadmin").PostAsJsonAsync("/api/organisation/countries",
            new CreateCountryRequest("PK", "Pakistan again", "PKR", "Asia/Karachi", 3, 30));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Out_of_range_values_return_400()
    {
        var response = await factory.CreateClientAs("dev-pk-systemadmin")
            .PutAsJsonAsync("/api/organisation/countries/PK", Pakistan(probation: 99));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("probationMonths", out _));
    }

    [Fact]
    public async Task Invalid_time_zone_returns_400()
    {
        var response = await factory.CreateClientAs("dev-pk-hradmin")
            .PutAsJsonAsync("/api/organisation/countries/PK", new UpdateCountrySettingsRequest("Pakistan", "PKT", 3, 30));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("defaultTimeZone", out _));
    }

    [Fact]
    public async Task Unknown_country_returns_404()
    {
        var response = await factory.CreateClientAs("dev-pk-hradmin")
            .PutAsJsonAsync("/api/organisation/countries/GB", new UpdateCountrySettingsRequest("United Kingdom", "Europe/London", 3, 30));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
