using HrPortal.Api.Auth;
using HrPortal.Domain.Common;
using HrPortal.Domain.Organisation;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Organisation;

/// <summary>
/// Legal entities, office locations, departments and grades (MC-01). Anyone signed in can read them (they label
/// employee records and fill pickers); only HR Admins change them, and only in countries they can access.
/// Items in use cannot be deleted (409); deactivate them instead.
/// </summary>
public static class OrganisationEndpoints
{
    public sealed record LegalEntityResponse(Guid Id, string CountryCode, string Name, bool IsActive);

    public sealed record CreateLegalEntityRequest(string CountryCode, string Name);

    public sealed record UpdateLegalEntityRequest(string Name, bool IsActive);

    public sealed record OfficeLocationResponse(
        Guid Id, string CountryCode, string Name, string? City, string? TimeZone, string EffectiveTimeZone, bool IsActive);

    public sealed record CreateOfficeLocationRequest(string CountryCode, string Name, string? City, string? TimeZone);

    public sealed record UpdateOfficeLocationRequest(string Name, string? City, string? TimeZone, bool IsActive);

    public sealed record DepartmentResponse(Guid Id, string Code, string Name, bool IsActive);

    public sealed record CreateDepartmentRequest(string Code, string Name);

    public sealed record UpdateDepartmentRequest(string Code, string Name, bool IsActive);

    public sealed record GradeResponse(
        Guid Id,
        string CountryCode,
        string Code,
        string Name,
        int Level,
        int? ProbationMonths,
        int? NoticePeriodDays,
        int EffectiveProbationMonths,
        int EffectiveNoticePeriodDays,
        bool IsActive);

    public sealed record CreateGradeRequest(
        string CountryCode, string Code, string Name, int Level, int? ProbationMonths, int? NoticePeriodDays);

    public sealed record UpdateGradeRequest(
        string Code, string Name, int Level, int? ProbationMonths, int? NoticePeriodDays, bool IsActive);

    public static IEndpointRouteBuilder MapOrganisationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organisation").WithTags("Organisation");
        MapLegalEntities(group.MapGroup("/legal-entities"));
        MapOfficeLocations(group.MapGroup("/locations"));
        MapDepartments(group.MapGroup("/departments"));
        MapGrades(group.MapGroup("/grades"));
        return app;
    }

    private static void MapLegalEntities(RouteGroupBuilder group)
    {
        group.MapGet("/", async (HrPortalDbContext db, CancellationToken ct) =>
                (await db.LegalEntities.AsNoTracking().OrderBy(e => e.CountryCode).ThenBy(e => e.Name).ToListAsync(ct))
                .Select(ToResponse))
            .RequireAuthorization(Policies.Authenticated);

        group.MapPost("/", async (CreateLegalEntityRequest request, HrPortalDbContext db, IAccessContext access, CancellationToken ct) =>
            {
                var entity = new LegalEntity(request.CountryCode, request.Name);
                if (await DenyCountryAsync(entity.CountryCode, db, access, ct) is { } denied)
                {
                    return denied;
                }

                db.LegalEntities.Add(entity);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/organisation/legal-entities/{entity.Id}", ToResponse(entity));
            })
            .RequireAuthorization(Policies.ManageOrganisation);

        group.MapPut("/{id:guid}", async (Guid id, UpdateLegalEntityRequest request, HrPortalDbContext db, IAccessContext access, CancellationToken ct) =>
            {
                var entity = await db.LegalEntities.FindAsync([id], ct);
                if (entity is null)
                {
                    return Results.NotFound();
                }

                if (await DenyCountryAsync(entity.CountryCode, db, access, ct) is { } denied)
                {
                    return denied;
                }

                entity.Update(request.Name, request.IsActive);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(entity));
            })
            .RequireAuthorization(Policies.ManageOrganisation);

        group.MapDelete("/{id:guid}", async (Guid id, HrPortalDbContext db, IAccessContext access, CancellationToken ct) =>
                await DeleteAsync(db, access, await db.LegalEntities.FindAsync([id], ct), e => e.CountryCode, ct))
            .RequireAuthorization(Policies.ManageOrganisation);
    }

    private static void MapOfficeLocations(RouteGroupBuilder group)
    {
        group.MapGet("/", async (HrPortalDbContext db, CancellationToken ct) =>
            {
                var countries = await db.CountrySettings.AsNoTracking().ToDictionaryAsync(c => c.CountryCode, ct);
                var locations = await db.OfficeLocations.AsNoTracking().OrderBy(l => l.CountryCode).ThenBy(l => l.Name).ToListAsync(ct);
                return locations.Select(l => ToResponse(l, countries[l.CountryCode]));
            })
            .RequireAuthorization(Policies.Authenticated);

        group.MapPost("/", async (CreateOfficeLocationRequest request, HrPortalDbContext db, IAccessContext access, CancellationToken ct) =>
            {
                var location = new OfficeLocation(request.CountryCode, request.Name, request.City, request.TimeZone);
                if (await DenyCountryAsync(location.CountryCode, db, access, ct) is { } denied)
                {
                    return denied;
                }

                db.OfficeLocations.Add(location);
                await db.SaveChangesAsync(ct);
                var country = await db.CountrySettings.FindAsync([location.CountryCode], ct);
                return Results.Created($"/api/organisation/locations/{location.Id}", ToResponse(location, country!));
            })
            .RequireAuthorization(Policies.ManageOrganisation);

        group.MapPut("/{id:guid}", async (Guid id, UpdateOfficeLocationRequest request, HrPortalDbContext db, IAccessContext access, CancellationToken ct) =>
            {
                var location = await db.OfficeLocations.FindAsync([id], ct);
                if (location is null)
                {
                    return Results.NotFound();
                }

                if (await DenyCountryAsync(location.CountryCode, db, access, ct) is { } denied)
                {
                    return denied;
                }

                location.Update(request.Name, request.City, request.TimeZone, request.IsActive);
                await db.SaveChangesAsync(ct);
                var country = await db.CountrySettings.FindAsync([location.CountryCode], ct);
                return Results.Ok(ToResponse(location, country!));
            })
            .RequireAuthorization(Policies.ManageOrganisation);

        group.MapDelete("/{id:guid}", async (Guid id, HrPortalDbContext db, IAccessContext access, CancellationToken ct) =>
                await DeleteAsync(db, access, await db.OfficeLocations.FindAsync([id], ct), l => l.CountryCode, ct))
            .RequireAuthorization(Policies.ManageOrganisation);
    }

    private static void MapDepartments(RouteGroupBuilder group)
    {
        group.MapGet("/", async (HrPortalDbContext db, CancellationToken ct) =>
                (await db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct)).Select(ToResponse))
            .RequireAuthorization(Policies.Authenticated);

        group.MapPost("/", async (CreateDepartmentRequest request, HrPortalDbContext db, CancellationToken ct) =>
            {
                var department = new Department(request.Code, request.Name);
                db.Departments.Add(department);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/organisation/departments/{department.Id}", ToResponse(department));
            })
            .RequireAuthorization(Policies.ManageOrganisation);

        group.MapPut("/{id:guid}", async (Guid id, UpdateDepartmentRequest request, HrPortalDbContext db, CancellationToken ct) =>
            {
                var department = await db.Departments.FindAsync([id], ct);
                if (department is null)
                {
                    return Results.NotFound();
                }

                department.Update(request.Code, request.Name, request.IsActive);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(department));
            })
            .RequireAuthorization(Policies.ManageOrganisation);

        // Departments span countries, so they are not limited by the HR Admin's country scope.
        group.MapDelete("/{id:guid}", async (Guid id, HrPortalDbContext db, IAccessContext access, CancellationToken ct) =>
                await DeleteAsync(db, access, await db.Departments.FindAsync([id], ct), countryOf: null, ct))
            .RequireAuthorization(Policies.ManageOrganisation);
    }

    private static void MapGrades(RouteGroupBuilder group)
    {
        group.MapGet("/", async (HrPortalDbContext db, CancellationToken ct) =>
            {
                var countries = await db.CountrySettings.AsNoTracking().ToDictionaryAsync(c => c.CountryCode, ct);
                var grades = await db.Grades.AsNoTracking().OrderBy(g => g.CountryCode).ThenBy(g => g.Level).ThenBy(g => g.Code).ToListAsync(ct);
                return grades.Select(g => ToResponse(g, countries[g.CountryCode]));
            })
            .RequireAuthorization(Policies.Authenticated);

        group.MapPost("/", async (CreateGradeRequest request, HrPortalDbContext db, IAccessContext access, CancellationToken ct) =>
            {
                var grade = new Grade(
                    request.CountryCode, request.Code, request.Name, request.Level, request.ProbationMonths, request.NoticePeriodDays);
                if (await DenyCountryAsync(grade.CountryCode, db, access, ct) is { } denied)
                {
                    return denied;
                }

                db.Grades.Add(grade);
                await db.SaveChangesAsync(ct);
                var country = await db.CountrySettings.FindAsync([grade.CountryCode], ct);
                return Results.Created($"/api/organisation/grades/{grade.Id}", ToResponse(grade, country!));
            })
            .RequireAuthorization(Policies.ManageOrganisation);

        group.MapPut("/{id:guid}", async (Guid id, UpdateGradeRequest request, HrPortalDbContext db, IAccessContext access, CancellationToken ct) =>
            {
                var grade = await db.Grades.FindAsync([id], ct);
                if (grade is null)
                {
                    return Results.NotFound();
                }

                if (await DenyCountryAsync(grade.CountryCode, db, access, ct) is { } denied)
                {
                    return denied;
                }

                grade.Update(request.Code, request.Name, request.Level, request.ProbationMonths, request.NoticePeriodDays, request.IsActive);
                await db.SaveChangesAsync(ct);
                var country = await db.CountrySettings.FindAsync([grade.CountryCode], ct);
                return Results.Ok(ToResponse(grade, country!));
            })
            .RequireAuthorization(Policies.ManageOrganisation);

        group.MapDelete("/{id:guid}", async (Guid id, HrPortalDbContext db, IAccessContext access, CancellationToken ct) =>
                await DeleteAsync(db, access, await db.Grades.FindAsync([id], ct), g => g.CountryCode, ct))
            .RequireAuthorization(Policies.ManageOrganisation);
    }

    /// <summary>403 if the user may not configure the country; 400 if the country does not exist; otherwise null.</summary>
    private static async Task<IResult?> DenyCountryAsync(string countryCode, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct)
    {
        if (!(await accessContext.GetAsync(ct)).CanConfigureCountry(countryCode))
        {
            return Results.Forbid();
        }

        if (!await db.CountrySettings.AnyAsync(c => c.CountryCode == countryCode, ct))
        {
            throw new DomainValidationException(nameof(countryCode), "Unknown country. Add it under Countries first.");
        }

        return null;
    }

    private static async Task<IResult> DeleteAsync<T>(
        HrPortalDbContext db, IAccessContext access, T? item, Func<T, string>? countryOf, CancellationToken ct)
        where T : class
    {
        if (item is null)
        {
            return Results.NotFound();
        }

        if (countryOf is not null && await DenyCountryAsync(countryOf(item), db, access, ct) is { } denied)
        {
            return denied;
        }

        db.Remove(item);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static LegalEntityResponse ToResponse(LegalEntity e) => new(e.Id, e.CountryCode, e.Name, e.IsActive);

    private static OfficeLocationResponse ToResponse(OfficeLocation l, CountrySettings country) =>
        new(l.Id, l.CountryCode, l.Name, l.City, l.TimeZone, l.EffectiveTimeZone(country), l.IsActive);

    private static DepartmentResponse ToResponse(Department d) => new(d.Id, d.Code, d.Name, d.IsActive);

    private static GradeResponse ToResponse(Grade g, CountrySettings country)
    {
        var terms = EmploymentTerms.For(country, g);
        return new(g.Id, g.CountryCode, g.Code, g.Name, g.Level, g.ProbationMonths, g.NoticePeriodDays,
            terms.ProbationMonths, terms.NoticePeriodDays, g.IsActive);
    }
}
