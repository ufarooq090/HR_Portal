using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Recruiting;

/// <summary>An application with its requisition and what the signed-in user may do with it.</summary>
internal sealed record ApplicationAccess(Application Application, Requisition Requisition, ApplicationPermissions Permissions, bool IsPanelist);

internal static class RecruitingAccess
{
    /// <summary>
    /// Loads an application (tracked, with its history) and the user's permissions on it. Returns 404 if it does not
    /// exist and 403 if the user may not see it, so out-of-scope ids are refused explicitly (NFR-02).
    /// </summary>
    public static async Task<(ApplicationAccess? Access, IResult? Denied)> LoadAsync(
        HrPortalDbContext db, UserAccess user, Guid applicationId, CancellationToken ct)
    {
        var application = await db.Applications.Include(a => a.History).SingleOrDefaultAsync(a => a.Id == applicationId, ct);
        if (application is null)
        {
            return (null, Results.NotFound());
        }

        var requisition = await db.Requisitions.SingleAsync(r => r.Id == application.RequisitionId, ct);
        var isPanelist = user.EmployeeId is { } me && await IsPanelistAsync(db, application.Id, me, ct);
        var permissions = RecruitingRules.For(
            user, new ApplicationFacts(requisition.CountryCode, requisition.RequesterEmployeeId, requisition.HiringManagerEmployeeId, isPanelist));

        return permissions.CanView
            ? (new ApplicationAccess(application, requisition, permissions, isPanelist), null)
            : (null, Results.Forbid());
    }

    public static Task<bool> IsPanelistAsync(HrPortalDbContext db, Guid applicationId, Guid employeeId, CancellationToken ct) =>
        db.Interviews.AnyAsync(
            i => i.ApplicationId == applicationId && i.Status == InterviewStatus.Scheduled && i.Panel.Any(p => p.EmployeeId == employeeId), ct);

    /// <summary>The countries' currencies (PKR, USD): candidate salary figures must be in one of them (MC-02).</summary>
    public static async Task<IReadOnlySet<string>> CurrenciesAsync(HrPortalDbContext db, CancellationToken ct) =>
        (await db.CountrySettings.Select(c => c.CurrencyCode).Distinct().ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
}
