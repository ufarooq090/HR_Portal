using HrPortal.Domain.Common;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Auth;

public static class MeEndpoints
{
    /// <param name="HrCountries">Countries the user sees as HR staff; empty for non-HR users.</param>
    /// <param name="IsApprover">Whether the user is in any requisition approval chain (shows the Approvals page).</param>
    public sealed record MeResponse(
        string UserId,
        string DisplayName,
        IReadOnlyList<string> Roles,
        Guid? EmployeeId,
        string? CountryCode,
        HrCountriesResponse HrCountries,
        bool IsApprover);

    public sealed record HrCountriesResponse(bool All, IReadOnlyList<string> Codes);

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", async (ICurrentUser user, IAccessContext accessContext, HrPortalDbContext db, CancellationToken ct) =>
            {
                var access = await accessContext.GetAsync(ct);

                // A handful of small chains: checked in memory rather than querying inside the JSON column.
                var isApprover = access.EmployeeId is { } me
                    && (await db.ApprovalChains.AsNoTracking().ToListAsync(ct)).Any(c => c.Steps.Any(s => s.ApproverEmployeeId == me));

                return new MeResponse(
                    user.UserId,
                    user.DisplayName,
                    user.Roles.Order().ToList(),
                    access.EmployeeId,
                    access.HomeCountryCode,
                    new HrCountriesResponse(access.HrCountries.IsAll, access.HrCountries.Codes),
                    isApprover);
            })
            .RequireAuthorization(Policies.Authenticated)
            .WithTags("Me");

        return app;
    }
}
