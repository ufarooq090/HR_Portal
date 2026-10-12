using HrPortal.Domain.Common;

namespace HrPortal.Api.Auth;

public static class MeEndpoints
{
    public sealed record MeResponse(string UserId, string DisplayName, IReadOnlyList<string> Roles);

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", (ICurrentUser user) =>
                new MeResponse(user.UserId, user.DisplayName, user.Roles.Order().ToList()))
            .RequireAuthorization(Policies.Authenticated)
            .WithTags("Me");

        return app;
    }
}
