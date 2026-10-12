namespace HrPortal.Api.Auth.DevAuth;

public static class DevAuthEndpoints
{
    /// <summary>
    /// Lists seeded dev users for the SPA's sign-in picker. Only mapped when DevAuth is enabled,
    /// which <see cref="DevAuthGuard"/> restricts to Development and Test.
    /// </summary>
    public static IEndpointRouteBuilder MapDevAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dev/users", () => DevUsers.All)
            .AllowAnonymous()
            .WithTags("DevAuth");

        return app;
    }
}
