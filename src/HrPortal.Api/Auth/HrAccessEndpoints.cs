using HrPortal.Domain.Common;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Auth;

/// <summary>
/// HR Admins limit or extend other HR users' country access and grant compensation access (MC-03).
/// Rules (in <see cref="UserAccess.CanGrantHrAccess"/>): never your own, only within your own countries.
/// </summary>
public static class HrAccessEndpoints
{
    public sealed record HrAccessResponse(
        Guid EmployeeId,
        string EmployeeNumber,
        string EmployeeName,
        string EmployeeCountryCode,
        bool AllCountries,
        IReadOnlyList<string> CountryCodes,
        bool CompensationAccess);

    public sealed record UpdateHrAccessRequest(bool AllCountries, IReadOnlyList<string>? CountryCodes, bool CompensationAccess);

    public static IEndpointRouteBuilder MapHrAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/access/hr").WithTags("Access").RequireAuthorization(Policies.ManageHrAccess);

        group.MapGet("/", async (HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct) =>
        {
            var access = await accessContext.GetAsync(ct);
            var rows = await ListAsync(db, employeeId: null, ct);
            return rows.Where(r => access.HrCountries.Includes(r.EmployeeCountryCode));
        });

        group.MapPut("/{employeeId:guid}", async (
            Guid employeeId, UpdateHrAccessRequest request, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct) =>
        {
            var target = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId)
                .Select(e => new { e.CountryCode }).SingleOrDefaultAsync(ct);
            if (target is null)
            {
                return Results.NotFound();
            }

            var requested = request.AllCountries ? CountryScope.All : CountryScope.Of(request.CountryCodes ?? []);
            var access = await accessContext.GetAsync(ct);
            if (!access.CanGrantHrAccess(employeeId, target.CountryCode, requested))
            {
                return Results.Forbid();
            }

            var known = await db.CountrySettings.Select(c => c.CountryCode).ToListAsync(ct);
            var unknown = requested.Codes.Except(known).ToList();
            if (unknown.Count > 0)
            {
                throw new DomainValidationException("countryCodes", $"Unknown country: {string.Join(", ", unknown)}.");
            }

            var settings = await db.HrAccessSettings.FindAsync([employeeId], ct);
            if (settings is null)
            {
                db.HrAccessSettings.Add(new HrAccessSettings(employeeId, requested, request.CompensationAccess));
            }
            else
            {
                settings.Update(requested, request.CompensationAccess);
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok((await ListAsync(db, employeeId, ct)).Single());
        });

        // Resetting falls back to the role default, which is every country for an HR Admin,
        // so only an HR Admin who can see every country may reset.
        group.MapDelete("/{employeeId:guid}", async (Guid employeeId, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct) =>
        {
            var settings = await db.HrAccessSettings.FindAsync([employeeId], ct);
            var country = await db.Employees.Where(e => e.Id == employeeId).Select(e => e.CountryCode).SingleOrDefaultAsync(ct);
            if (settings is null || country is null)
            {
                return Results.NotFound();
            }

            if (!(await accessContext.GetAsync(ct)).CanGrantHrAccess(employeeId, country, CountryScope.All))
            {
                return Results.Forbid();
            }

            db.HrAccessSettings.Remove(settings);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<List<HrAccessResponse>> ListAsync(HrPortalDbContext db, Guid? employeeId, CancellationToken ct)
    {
        var rows = await (
                from s in db.HrAccessSettings.AsNoTracking()
                join e in db.Employees on s.EmployeeId equals e.Id
                where employeeId == null || e.Id == employeeId
                orderby e.Person.GivenName, e.Person.FamilyName
                select new { e.Id, e.EmployeeNumber, e.Person.GivenName, e.Person.FamilyName, e.CountryCode, s.CountryCodes, s.CompensationAccess })
            .ToListAsync(ct);

        return rows.Select(r => new HrAccessResponse(
                r.Id, r.EmployeeNumber, $"{r.GivenName} {r.FamilyName}", r.CountryCode, r.CountryCodes is null, r.CountryCodes ?? [], r.CompensationAccess))
            .ToList();
    }
}
