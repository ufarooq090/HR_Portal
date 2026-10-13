using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using HrPortal.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Employees;

/// <summary>Per-record scope checks for employee sub-resources, evaluated in SQL by <see cref="EmployeeScopeQueries"/>.</summary>
internal static class EmployeeScope
{
    public static Task<bool> CoversAsync(
        HrPortalDbContext db, UserAccess access, Guid employeeId, IReadOnlySet<DataScope> scopes, CancellationToken ct) =>
        db.Employees.WithinScopes(db, access, scopes).AnyAsync(e => e.Id == employeeId, ct);

    /// <summary>Null when the employee is in scope; otherwise 403 if they exist (the spec asks for 403) or 404.</summary>
    public static async Task<IResult?> DenyAsync(
        HrPortalDbContext db, UserAccess access, Guid employeeId, IReadOnlySet<DataScope> scopes, CancellationToken ct)
    {
        if (await CoversAsync(db, access, employeeId, scopes, ct))
        {
            return null;
        }

        return await db.Employees.AnyAsync(e => e.Id == employeeId, ct) ? Results.Forbid() : Results.NotFound();
    }

    /// <summary>HR may only place employees in countries they can access (MC-03).</summary>
    public static bool CanPlaceIn(UserAccess access, Capability capability, string countryCode) =>
        access.ScopesFor(HrModule.EmployeeRecords, capability).Contains(DataScope.Countries) && access.HrCountries.Includes(countryCode);
}
