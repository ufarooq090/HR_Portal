using HrPortal.Api.Auth;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Recruiting;

/// <summary>CT-03: the configurable list of rejection reasons. HR Admins manage it; anyone in recruiting reads it.</summary>
public static class RejectionReasonEndpoints
{
    public sealed record RejectionReasonDto(Guid Id, string Name, int SortOrder, bool IsActive);

    public sealed record RejectionReasonRequest(string Name, int SortOrder, bool IsActive = true);

    public static IEndpointRouteBuilder MapRejectionReasonEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recruiting/rejection-reasons").WithTags("Recruiting");

        group.MapGet("/", async (HrPortalDbContext db, CancellationToken ct) =>
                await db.RejectionReasons.AsNoTracking().OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
                    .Select(r => new RejectionReasonDto(r.Id, r.Name, r.SortOrder, r.IsActive))
                    .ToListAsync(ct))
            .RequireAuthorization(Policies.Module(HrModule.Candidates, Capability.View));

        group.MapPost("/", async (RejectionReasonRequest request, HrPortalDbContext db, CancellationToken ct) =>
            {
                var reason = new RejectionReason(request.Name, request.SortOrder);
                db.RejectionReasons.Add(reason);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/recruiting/rejection-reasons/{reason.Id}", new RejectionReasonDto(reason.Id, reason.Name, reason.SortOrder, reason.IsActive));
            })
            .RequireAuthorization(Policies.ManageOrganisation);

        group.MapPut("/{id:guid}", async (Guid id, RejectionReasonRequest request, HrPortalDbContext db, CancellationToken ct) =>
            {
                var reason = await db.RejectionReasons.FindAsync([id], ct);
                if (reason is null)
                {
                    return Results.NotFound();
                }

                reason.Update(request.Name, request.SortOrder, request.IsActive);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new RejectionReasonDto(reason.Id, reason.Name, reason.SortOrder, reason.IsActive));
            })
            .RequireAuthorization(Policies.ManageOrganisation);

        // Reasons already used in a stage history cannot be deleted (409); deactivate them instead.
        group.MapDelete("/{id:guid}", async (Guid id, HrPortalDbContext db, CancellationToken ct) =>
            {
                var reason = await db.RejectionReasons.FindAsync([id], ct);
                if (reason is null)
                {
                    return Results.NotFound();
                }

                db.RejectionReasons.Remove(reason);
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.ManageOrganisation);

        return app;
    }
}
