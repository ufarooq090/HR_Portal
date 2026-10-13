using HrPortal.Domain.Common;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Infrastructure.Security;

/// <summary>Links the signed-in account to its employee record and HR access settings. Scoped per request.</summary>
public sealed class AccessContext(HrPortalDbContext db, ICurrentUser currentUser) : IAccessContext
{
    private UserAccess? _access;

    public async Task<UserAccess> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_access is not null)
        {
            return _access;
        }

        var employee = currentUser.IsAuthenticated
            ? await db.Employees.AsNoTracking()
                .Where(e => e.UserObjectId == currentUser.UserId)
                .Select(e => new { e.Id, e.DepartmentId, e.CountryCode })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var settings = employee is null
            ? null
            : await db.HrAccessSettings.AsNoTracking().SingleOrDefaultAsync(s => s.EmployeeId == employee.Id, cancellationToken);

        _access = new UserAccess(currentUser.Roles, employee?.Id, employee?.DepartmentId, employee?.CountryCode, settings);
        return _access;
    }
}
