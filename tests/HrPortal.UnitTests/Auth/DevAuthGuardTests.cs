using HrPortal.Api.Auth.DevAuth;
using HrPortal.Domain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HrPortal.UnitTests.Auth;

public class DevAuthGuardTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "HrPortal.Api";

        public string ContentRootPath { get; set; } = ".";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(bool enabled) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [DevAuthGuard.ConfigKey] = enabled.ToString() })
        .Build();

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("UAT")]
    public void Enabling_DevAuth_outside_Development_or_Test_fails_startup(string environment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DevAuthGuard.Check(Config(true), new FakeEnvironment(environment)));

        Assert.Contains(environment, ex.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void DevAuth_is_allowed_in_Development_and_Test(string environment)
    {
        Assert.True(DevAuthGuard.Check(Config(true), new FakeEnvironment(environment)));
    }

    [Fact]
    public void DevAuth_disabled_in_Production_is_fine()
    {
        Assert.False(DevAuthGuard.Check(Config(false), new FakeEnvironment("Production")));
    }

    [Fact]
    public void There_is_a_seeded_user_for_every_role_in_both_countries()
    {
        foreach (var country in new[] { "PK", "US" })
        {
            foreach (var role in Roles.All)
            {
                Assert.Contains(DevUsers.All, u => u.CountryCode == country && u.Roles[0] == role);
            }
        }
    }
}
