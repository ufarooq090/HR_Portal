namespace HrPortal.Api.Auth.DevAuth;

public static class DevAuthGuard
{
    public const string ConfigKey = "DevAuth:Enabled";
    public const string TestEnvironmentName = "Test";

    /// <summary>Throws if DevAuth is enabled outside the Development or Test environment.</summary>
    /// <returns>Whether DevAuth is enabled.</returns>
    public static bool Check(IConfiguration configuration, IHostEnvironment environment)
    {
        var enabled = configuration.GetValue<bool>(ConfigKey);
        if (enabled && !environment.IsDevelopment() && !environment.IsEnvironment(TestEnvironmentName))
        {
            throw new InvalidOperationException(
                $"DevAuth is enabled in the '{environment.EnvironmentName}' environment. " +
                "It may only be enabled in Development or Test. Remove 'DevAuth:Enabled' from configuration.");
        }

        return enabled;
    }
}
