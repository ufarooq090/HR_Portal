using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HrPortal.Domain.Audit;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Organisation.CountrySettingsEndpoints;

namespace HrPortal.IntegrationTests.Organisation;

[Collection(ApiCollection.Name)]
public class CountrySettingsEndpointTests(HrPortalApiFactory factory)
{
    [Fact]
    public async Task Any_signed_in_user_can_read_country_settings()
    {
        var countries = await factory.CreateClientAs("dev-pk-employee")
            .GetFromJsonAsync<List<CountrySettingsResponse>>("/api/organisation/countries");

        Assert.NotNull(countries);
        Assert.Equal(["PK", "US"], countries.Select(c => c.CountryCode));
        Assert.Equal(["PKR", "USD"], countries.Select(c => c.CurrencyCode));
    }

    [Theory]
    [InlineData("dev-pk-employee")]
    [InlineData("dev-pk-hrexecutive")]
    [InlineData("dev-us-linemanager")]
    [InlineData("dev-us-hiringmanager")]
    public async Task Users_without_admin_role_cannot_change_country_settings(string devUser)
    {
        var response = await factory.CreateClientAs(devUser)
            .PutAsJsonAsync("/api/organisation/countries/PK", new UpdateCountrySettingsRequest(1, 1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HR_admin_change_is_saved_and_audited_with_old_and_new_values()
    {
        var client = factory.CreateClientAs("dev-us-hradmin");

        var response = await client.PutAsJsonAsync("/api/organisation/countries/US", new UpdateCountrySettingsRequest(6, 14));

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

        await client.PutAsJsonAsync("/api/organisation/countries/US", new UpdateCountrySettingsRequest(3, 30));
    }

    [Fact]
    public async Task Out_of_range_values_return_400()
    {
        var response = await factory.CreateClientAs("dev-pk-systemadmin")
            .PutAsJsonAsync("/api/organisation/countries/PK", new UpdateCountrySettingsRequest(99, 30));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("probationMonths", out _));
    }

    [Fact]
    public async Task Unknown_country_returns_404()
    {
        var response = await factory.CreateClientAs("dev-pk-hradmin")
            .PutAsJsonAsync("/api/organisation/countries/GB", new UpdateCountrySettingsRequest(3, 30));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
