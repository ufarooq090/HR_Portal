using System.Net;
using System.Net.Http.Json;
using HrPortal.Api.Auth;
using HrPortal.Api.Auth.DevAuth;
using HrPortal.IntegrationTests.Infrastructure;

namespace HrPortal.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public class AuthenticationTests(HrPortalApiFactory factory)
{
    [Fact]
    public async Task Health_is_anonymous()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_request_to_api_is_rejected()
    {
        var response = await factory.CreateClient().GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_dev_user_is_rejected()
    {
        var response = await factory.CreateClientAs("not-a-seeded-user").GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dev_user_list_is_available_for_the_sign_in_picker()
    {
        var users = await factory.CreateClient().GetFromJsonAsync<List<DevUser>>("/api/dev/users");

        Assert.NotNull(users);
        Assert.Equal(14, users.Count);
        Assert.Contains(users, u => u.Id == "dev-pk-hradmin" && u.CountryCode == "PK");
    }

    [Fact]
    public async Task Me_returns_the_signed_in_users_roles()
    {
        var me = await factory.CreateClientAs("dev-us-linemanager").GetFromJsonAsync<MeEndpoints.MeResponse>("/api/me");

        Assert.NotNull(me);
        Assert.Equal("dev-us-linemanager", me.UserId);
        Assert.Equal(["Employee", "LineManager"], me.Roles);
    }
}
