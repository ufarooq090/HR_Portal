using HrPortal.Api.Auth;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using HrPortal.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Employees;

/// <summary>
/// Employee directory (EMP-08), scoped per the permission matrix: HR sees their countries, managers their
/// team (direct and indirect reports), everyone else only themselves. Scoping runs in the query (NFR-02).
/// </summary>
public static class EmployeeEndpoints
{
    public sealed record EmployeeSummary(
        Guid Id,
        string EmployeeNumber,
        string FullName,
        string JobTitle,
        string WorkEmail,
        string CountryCode,
        Guid DepartmentId,
        string DepartmentName,
        Guid OfficeLocationId,
        string OfficeLocationName,
        Guid? ManagerId,
        string? ManagerName);

    public sealed record EmployeeDetail(
        Guid Id,
        string EmployeeNumber,
        string GivenName,
        string FamilyName,
        string JobTitle,
        string WorkEmail,
        string CountryCode,
        Guid LegalEntityId,
        string LegalEntityName,
        Guid OfficeLocationId,
        string OfficeLocationName,
        Guid DepartmentId,
        string DepartmentName,
        Guid? GradeId,
        string? GradeName,
        Guid? ManagerId,
        string? ManagerName,
        DateOnly JoiningDate);

    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/employees").WithTags("Employees");
        var view = Policies.Module(HrModule.EmployeeRecords, Capability.View);

        group.MapGet("/", async (string? search, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct) =>
            {
                var access = await accessContext.GetAsync(ct);
                var query = db.Employees.AsNoTracking().VisibleTo(db, access, HrModule.EmployeeRecords, Capability.View);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var pattern = $"%{EscapeLike(search.Trim())}%";
                    query = query.Where(e =>
                        EF.Functions.ILike(e.Person.GivenName + " " + e.Person.FamilyName, pattern)
                        || EF.Functions.ILike(e.EmployeeNumber, pattern)
                        || EF.Functions.ILike(e.WorkEmail, pattern));
                }

                return await query
                    .OrderBy(e => e.Person.GivenName).ThenBy(e => e.Person.FamilyName).ThenBy(e => e.EmployeeNumber)
                    .Select(e => new EmployeeSummary(
                        e.Id,
                        e.EmployeeNumber,
                        e.Person.GivenName + " " + e.Person.FamilyName,
                        e.JobTitle,
                        e.WorkEmail,
                        e.CountryCode,
                        e.DepartmentId,
                        e.Department.Name,
                        e.OfficeLocationId,
                        e.OfficeLocation.Name,
                        e.ManagerId,
                        e.Manager == null ? null : e.Manager.Person.GivenName + " " + e.Manager.Person.FamilyName))
                    .ToListAsync(ct);
            })
            .RequireAuthorization(view);

        group.MapGet("/{id:guid}", async (Guid id, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct) =>
            {
                var access = await accessContext.GetAsync(ct);
                var employee = await db.Employees.AsNoTracking()
                    .VisibleTo(db, access, HrModule.EmployeeRecords, Capability.View)
                    .Where(e => e.Id == id)
                    .Select(e => new EmployeeDetail(
                        e.Id,
                        e.EmployeeNumber,
                        e.Person.GivenName,
                        e.Person.FamilyName,
                        e.JobTitle,
                        e.WorkEmail,
                        e.CountryCode,
                        e.LegalEntityId,
                        e.LegalEntity.Name,
                        e.OfficeLocationId,
                        e.OfficeLocation.Name,
                        e.DepartmentId,
                        e.Department.Name,
                        e.GradeId,
                        e.Grade == null ? null : e.Grade.Name,
                        e.ManagerId,
                        e.Manager == null ? null : e.Manager.Person.GivenName + " " + e.Manager.Person.FamilyName,
                        e.JoiningDate))
                    .SingleOrDefaultAsync(ct);

                if (employee is not null)
                {
                    return Results.Ok(employee);
                }

                // Out of scope is refused explicitly, as the spec requires (a manager reading another team gets 403).
                return await db.Employees.AnyAsync(e => e.Id == id, ct) ? Results.Forbid() : Results.NotFound();
            })
            .RequireAuthorization(view);

        return app;
    }

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
