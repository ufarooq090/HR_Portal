using HrPortal.Api.Auth.DevAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HrPortal.IntegrationTests.Auth;

public class DevAuthStartupGuardTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Api_refuses_to_start_with_DevAuth_outside_Development_and_Test(string environment)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting(DevAuthGuard.ConfigKey, "true");
        });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("DevAuth", ex.Message);
    }
}
