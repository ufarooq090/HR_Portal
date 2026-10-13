using System.Linq.Expressions;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;

namespace HrPortal.Infrastructure.Security;

/// <summary>
/// Applies a user's data scopes to employee queries, in SQL (NFR-02). Other modules scope their records by
/// joining to <see cref="VisibleTo"/> on the owning employee's id.
/// </summary>
public static class EmployeeScopeQueries
{
    /// <summary>Employees on whom <paramref name="access"/> holds <paramref name="capability"/> in <paramref name="module"/>.</summary>
    public static IQueryable<Employee> VisibleTo(
        this IQueryable<Employee> employees,
        HrPortalDbContext db,
        UserAccess access,
        HrModule module,
        Capability capability) =>
        employees.WithinScopes(db, access, access.ScopesFor(module, capability));

    public static IQueryable<Employee> WithinScopes(
        this IQueryable<Employee> employees,
        HrPortalDbContext db,
        UserAccess access,
        IReadOnlySet<DataScope> scopes)
    {
        if (scopes.Contains(DataScope.All) || (scopes.Contains(DataScope.Countries) && access.HrCountries.IsAll))
        {
            return employees;
        }

        var predicates = new List<Expression<Func<Employee, bool>>>();

        if (scopes.Contains(DataScope.Countries))
        {
            var countries = access.HrCountries.Codes.ToList();
            predicates.Add(e => countries.Contains(e.CountryCode));
        }

        if (scopes.Contains(DataScope.Department) && access.DepartmentId is { } departmentId)
        {
            predicates.Add(e => e.DepartmentId == departmentId);
        }

        if (access.EmployeeId is { } self)
        {
            if (scopes.Contains(DataScope.Team))
            {
                var reports = db.ReportIds(self);
                predicates.Add(e => reports.Contains(e.Id));
            }

            if (scopes.Contains(DataScope.Own))
            {
                predicates.Add(e => e.Id == self);
            }
        }

        // Assigned items (interviews, tasks) are not employee records; that scope grants no employees.
        return predicates.Count == 0 ? employees.Where(_ => false) : employees.Where(AnyOf(predicates));
    }

    private static Expression<Func<Employee, bool>> AnyOf(List<Expression<Func<Employee, bool>>> predicates)
    {
        var parameter = Expression.Parameter(typeof(Employee), "e");
        var body = predicates
            .Select(p => new ReplaceParameter(p.Parameters[0], parameter).Visit(p.Body))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<Employee, bool>>(body, parameter);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
