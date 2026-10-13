using HrPortal.Api.Auth;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Requisitions;

/// <summary>
/// REQ-02: approval chains per country, managed by HR Admins within their countries. Approvers may sit in any
/// country (e.g. the CEO approves for both). Read by HR staff to see who will approve.
/// </summary>
public static class ApprovalChainEndpoints
{
    public sealed record ApprovalStepDto(string Label, Guid ApproverEmployeeId, string ApproverName, bool ApproverActive);

    public sealed record ApprovalChainDto(string CountryCode, string CountryName, IReadOnlyList<ApprovalStepDto> Steps);

    public sealed record ApprovalStepRequest(string Label, Guid ApproverEmployeeId);

    public sealed record UpdateApprovalChainRequest(IReadOnlyList<ApprovalStepRequest> Steps);

    public static IEndpointRouteBuilder MapApprovalChainEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/approval-chains").WithTags("Requisitions");

        group.MapGet("/", async (HrPortalDbContext db, CancellationToken ct) => await ListAsync(db, ct))
            .RequireAuthorization(Policies.HrStaff);

        group.MapPut("/{countryCode}", async (
                string countryCode, UpdateApprovalChainRequest request, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct) =>
            {
                var code = countryCode.ToUpperInvariant();
                if (!await db.CountrySettings.AnyAsync(c => c.CountryCode == code, ct))
                {
                    return Results.NotFound();
                }

                if (!(await accessContext.GetAsync(ct)).CanConfigureCountry(code))
                {
                    return Results.Forbid();
                }

                var ids = (request.Steps ?? []).Select(s => s.ApproverEmployeeId).ToList();
                var approvers = await db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
                if (ids.Any(id => !approvers.ContainsKey(id)))
                {
                    throw new DomainValidationException("steps", "Choose existing employees as approvers.");
                }

                var steps = (request.Steps ?? []).Select(s => (s.Label, approvers[s.ApproverEmployeeId])).ToList();
                var chain = await db.ApprovalChains.FindAsync([code], ct);
                if (chain is null)
                {
                    db.ApprovalChains.Add(new ApprovalChain(code, steps));
                }
                else
                {
                    chain.Replace(steps);
                }

                await db.SaveChangesAsync(ct);
                return Results.Ok((await ListAsync(db, ct)).Single(c => c.CountryCode == code));
            })
            .RequireAuthorization(Policies.ManageOrganisation);

        return app;
    }

    /// <summary>Every country, with an empty list where no chain is set up yet.</summary>
    private static async Task<List<ApprovalChainDto>> ListAsync(HrPortalDbContext db, CancellationToken ct)
    {
        var countries = await db.CountrySettings.AsNoTracking().OrderBy(c => c.CountryCode).ToListAsync(ct);
        var chains = await db.ApprovalChains.AsNoTracking().ToDictionaryAsync(c => c.CountryCode, ct);
        var approverIds = chains.Values.SelectMany(c => c.Steps).Select(s => s.ApproverEmployeeId).Distinct().ToList();
        var approvers = await db.Employees.AsNoTracking().Where(e => approverIds.Contains(e.Id))
            .Select(e => new { e.Id, Name = e.Person.GivenName + " " + e.Person.FamilyName, e.Status })
            .ToDictionaryAsync(e => e.Id, ct);

        return countries.Select(c => new ApprovalChainDto(
                c.CountryCode,
                c.Name,
                chains.TryGetValue(c.CountryCode, out var chain)
                    ? chain.Steps.Select(s => new ApprovalStepDto(
                        s.Label, s.ApproverEmployeeId, approvers[s.ApproverEmployeeId].Name, approvers[s.ApproverEmployeeId].Status.IsActive())).ToList()
                    : []))
            .ToList();
    }
}
