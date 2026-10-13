using HrPortal.Domain.Common;
using HrPortal.Domain.Security;

namespace HrPortal.Api.Auth;

public static class MeEndpoints
{
    /// <param name="HrCountries">Countries the user sees as HR staff; empty for non-HR users.</param>
    public sealed record MeResponse(
        string UserId,
        string DisplayName,
        IReadOnlyList<string> Roles,
        Guid? EmployeeId,
        string? CountryCode,
        HrCountriesResponse HrCountries);

    public sealed record HrCountriesResponse(bool All, IReadOnlyList<string> Codes);

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", async (ICurrentUser user, IAccessContext accessContext, CancellationToken ct) =>
            {
                var access = await accessContext.GetAsync(ct);
                return new MeResponse(
                    user.UserId,
                    user.DisplayName,
                    user.Roles.Order().ToList(),
                    access.EmployeeId,
                    access.HomeCountryCode,
                    new HrCountriesResponse(access.HrCountries.IsAll, access.HrCountries.Codes));
            })
            .RequireAuthorization(Policies.Authenticated)
            .WithTags("Me");

        return app;
    }
}
