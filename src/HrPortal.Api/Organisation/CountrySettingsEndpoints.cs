using HrPortal.Api.Auth;
using HrPortal.Domain.Organisation;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Organisation;

public static class CountrySettingsEndpoints
{
    public sealed record CountrySettingsResponse(
        string CountryCode, string Name, string CurrencyCode, int ProbationMonths, int NoticePeriodDays);

    public sealed record UpdateCountrySettingsRequest(int ProbationMonths, int NoticePeriodDays);

    public static IEndpointRouteBuilder MapCountrySettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organisation/countries").WithTags("Organisation");

        group.MapGet("/", async (HrPortalDbContext db, CancellationToken ct) =>
                await db.CountrySettings
                    .AsNoTracking()
                    .OrderBy(c => c.CountryCode)
                    .Select(c => ToResponse(c))
                    .ToListAsync(ct))
            .RequireAuthorization(Policies.Authenticated);

        group.MapPut("/{countryCode}", async (
                string countryCode, UpdateCountrySettingsRequest request, HrPortalDbContext db, CancellationToken ct) =>
            {
                var country = await db.CountrySettings.FindAsync([countryCode.ToUpperInvariant()], ct);
                if (country is null)
                {
                    return Results.NotFound();
                }

                country.UpdateEmploymentDefaults(request.ProbationMonths, request.NoticePeriodDays);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(country));
            })
            .RequireAuthorization(Policies.ManageCountrySettings);

        return app;
    }

    private static CountrySettingsResponse ToResponse(CountrySettings c) =>
        new(c.CountryCode, c.Name, c.CurrencyCode, c.ProbationMonths, c.NoticePeriodDays);
}
