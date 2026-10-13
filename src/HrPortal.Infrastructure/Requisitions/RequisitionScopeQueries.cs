using System.Linq.Expressions;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;

namespace HrPortal.Infrastructure.Requisitions;

/// <summary>
/// Requisitions a user may see, applied in SQL (NFR-02): HR within their countries, managers their own department,
/// and anyone their own requests and the requests they are asked to approve.
/// </summary>
public static class RequisitionScopeQueries
{
    public static IQueryable<Requisition> VisibleTo(this IQueryable<Requisition> requisitions, UserAccess access)
    {
        var scopes = access.ScopesFor(HrModule.Requisitions, Capability.View);
        if (scopes.Contains(DataScope.All) || (scopes.Contains(DataScope.Countries) && access.HrCountries.IsAll))
        {
            return requisitions;
        }

        var predicates = new List<Expression<Func<Requisition, bool>>>();
        if (scopes.Contains(DataScope.Countries))
        {
            var countries = access.HrCountries.Codes.ToList();
            predicates.Add(r => countries.Contains(r.CountryCode));
        }

        if (scopes.Contains(DataScope.Department) && access.DepartmentId is { } department)
        {
            predicates.Add(r => r.DepartmentId == department);
        }

        if (access.EmployeeId is { } me)
        {
            predicates.Add(r => r.RequesterEmployeeId == me);
            predicates.Add(r => r.Approvals.Any(a => a.ApproverEmployeeId == me));
        }

        return predicates.Count == 0 ? requisitions.Where(_ => false) : requisitions.Where(AnyOf(predicates));
    }

    private static Expression<Func<Requisition, bool>> AnyOf(List<Expression<Func<Requisition, bool>>> predicates)
    {
        var parameter = Expression.Parameter(typeof(Requisition), "r");
        var body = predicates
            .Select(p => new ReplaceParameter(p.Parameters[0], parameter).Visit(p.Body))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<Requisition, bool>>(body, parameter);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
