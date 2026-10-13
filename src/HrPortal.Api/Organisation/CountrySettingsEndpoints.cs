using HrPortal.Api.Auth;
using HrPortal.Domain.Organisation;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Organisation;

public static class CountrySettingsEndpoints
{
    public sealed record CountrySettingsResponse(
        string CountryCode,
        string Name,
        string CurrencyCode,
        string DefaultTimeZone,
        int ProbationMonths,
        int NoticePeriodDays);

    public sealed record CreateCountryRequest(
        string CountryCode,
        string Name,
        string CurrencyCode,
        string DefaultTimeZone,
        int ProbationMonths,
        int NoticePeriodDays);

    public sealed record UpdateCountrySettingsRequest(string Name, string DefaultTimeZone, int ProbationMonths, int NoticePeriodDays);

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

        // A new country is visible to every user and affects every HR user's scope, so an HR Admin
        // limited to some countries cannot add one.
        group.MapPost("/", async (CreateCountryRequest request, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct) =>
            {
                var country = new CountrySettings(request.CountryCode, request.Name, request.CurrencyCode, request.DefaultTimeZone);
                country.UpdateEmploymentDefaults(request.ProbationMonths, request.NoticePeriodDays);

                var access = await accessContext.GetAsync(ct);
                if (!access.CanConfigureCountry(country.CountryCode))
                {
                    return Results.Forbid();
                }

                db.CountrySettings.Add(country);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/organisation/countries/{country.CountryCode}", ToResponse(country));
            })
            .RequireAuthorization(Policies.ManageCountrySettings);

        group.MapPut("/{countryCode}", async (
                string countryCode,
                UpdateCountrySettingsRequest request,
                HrPortalDbContext db,
                IAccessContext accessContext,
                CancellationToken ct) =>
            {
                var country = await db.CountrySettings.FindAsync([countryCode.ToUpperInvariant()], ct);
                if (country is null)
                {
                    return Results.NotFound();
                }

                if (!(await accessContext.GetAsync(ct)).CanConfigureCountry(country.CountryCode))
                {
                    return Results.Forbid();
                }

                country.UpdateDetails(request.Name, request.DefaultTimeZone);
                country.UpdateEmploymentDefaults(request.ProbationMonths, request.NoticePeriodDays);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(country));
            })
            .RequireAuthorization(Policies.ManageCountrySettings);

        return app;
    }

    private static CountrySettingsResponse ToResponse(CountrySettings c) =>
        new(c.CountryCode, c.Name, c.CurrencyCode, c.DefaultTimeZone, c.ProbationMonths, c.NoticePeriodDays);
}
