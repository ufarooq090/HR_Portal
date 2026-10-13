using System.Linq.Expressions;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;

namespace HrPortal.Infrastructure.Recruiting;

/// <summary>
/// Applications a user may see, applied in SQL (NFR-02): HR within their countries, hiring managers on their own
/// requisitions, and panelists on the candidates they interview. See <see cref="RecruitingRules"/>.
/// </summary>
public static class ApplicationScopeQueries
{
    public static IQueryable<Application> VisibleTo(this IQueryable<Application> applications, HrPortalDbContext db, UserAccess access)
    {
        var scopes = access.ScopesFor(HrModule.Candidates, Capability.View);
        if (scopes.Contains(DataScope.All) || (scopes.Contains(DataScope.Countries) && access.HrCountries.IsAll))
        {
            return applications;
        }

        var predicates = new List<Expression<Func<Application, bool>>>();
        if (scopes.Contains(DataScope.Countries))
        {
            var countries = access.HrCountries.Codes.ToList();
            predicates.Add(a => db.Requisitions.Any(r => r.Id == a.RequisitionId && countries.Contains(r.CountryCode)));
        }

        if (access.EmployeeId is { } me)
        {
            if (access.Roles.Contains(Roles.HiringManager))
            {
                predicates.Add(a => db.Requisitions.Any(r => r.Id == a.RequisitionId && (r.HiringManagerEmployeeId == me || r.RequesterEmployeeId == me)));
            }

            // Panel membership on a live interview is what assigns a candidate to an interviewer.
            predicates.Add(a => db.Interviews.Any(i =>
                i.ApplicationId == a.Id && i.Status == InterviewStatus.Scheduled && i.Panel.Any(p => p.EmployeeId == me)));
        }

        return predicates.Count == 0 ? applications.Where(_ => false) : applications.Where(AnyOf(predicates));
    }

    private static Expression<Func<Application, bool>> AnyOf(List<Expression<Func<Application, bool>>> predicates)
    {
        var parameter = Expression.Parameter(typeof(Application), "a");
        var body = predicates
            .Select(p => new ReplaceParameter(p.Parameters[0], parameter).Visit(p.Body))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<Application, bool>>(body, parameter);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
