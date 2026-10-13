using HrPortal.Api.Auth;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Employees;

/// <summary>
/// EMP-03 / MC-02: salary history in local currency. Visible to HR Admins, HR Executives with a grant (both within
/// their countries) and the employee; never to managers. Every read is audited as a view of salary data (NFR-04).
/// </summary>
public static class CompensationEndpoints
{
    public sealed record CompensationDto(
        Guid Id,
        DateOnly EffectiveDate,
        decimal AnnualCtc,
        string CurrencyCode,
        string? SalaryBand,
        CompensationReason Reason,
        string? Note,
        bool IsCurrent);

    public sealed record AddCompensationRequest(
        DateOnly EffectiveDate, decimal AnnualCtc, string CurrencyCode, string? SalaryBand, CompensationReason Reason, string? Note);

    public static IEndpointRouteBuilder MapCompensationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/employees/{id:guid}/compensation").WithTags("Employees");

        group.MapGet("/", async (Guid id, HrPortalDbContext db, IAccessContext accessContext, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
            {
                var access = await accessContext.GetAsync(ct);
                if (await EmployeeScope.DenyAsync(db, access, id, access.ScopesFor(HrModule.Compensation, Capability.View), ct) is { } denied)
                {
                    return denied;
                }

                await audit.RecordViewAsync(nameof(Compensation), id.ToString(), ct);
                return Results.Ok(await ListAsync(db, id, EmployeeEndpoints.Today(clock), ct));
            })
            .RequireAuthorization(Policies.Module(HrModule.Compensation, Capability.View));

        group.MapPost("/", async (
                Guid id, AddCompensationRequest request, HrPortalDbContext db, IAccessContext accessContext, TimeProvider clock, CancellationToken ct) =>
            {
                var access = await accessContext.GetAsync(ct);
                if (await EmployeeScope.DenyAsync(db, access, id, access.ScopesFor(HrModule.Compensation, Capability.Create), ct) is { } denied)
                {
                    return denied;
                }

                var employee = await db.Employees.SingleAsync(e => e.Id == id, ct);
                var country = await db.CountrySettings.SingleAsync(c => c.CountryCode == employee.CountryCode, ct);
                Money amount;
                try
                {
                    amount = new Money(request.AnnualCtc, (request.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant());
                }
                catch (ArgumentException)
                {
                    throw new DomainValidationException("currencyCode", "Use a three-letter currency code.");
                }

                db.Compensation.Add(Compensation.Create(
                    employee, country, amount, request.SalaryBand, request.EffectiveDate, request.Reason, request.Note));
                await db.SaveChangesAsync(ct);
                return Results.Ok(await ListAsync(db, id, EmployeeEndpoints.Today(clock), ct));
            })
            .RequireAuthorization(Policies.Module(HrModule.Compensation, Capability.Create));

        return app;
    }

    /// <summary>Newest first. The current record is the latest one already in effect.</summary>
    private static async Task<List<CompensationDto>> ListAsync(HrPortalDbContext db, Guid employeeId, DateOnly today, CancellationToken ct)
    {
        var records = await db.Compensation.AsNoTracking()
            .Where(c => c.EmployeeId == employeeId)
            .OrderByDescending(c => c.EffectiveDate).ThenByDescending(c => c.Id)
            .ToListAsync(ct);
        var current = records.FirstOrDefault(c => c.EffectiveDate <= today);

        return records
            .Select(c => new CompensationDto(
                c.Id, c.EffectiveDate, c.AnnualCtc.Amount, c.CurrencyCode, c.SalaryBand, c.Reason, c.Note, ReferenceEquals(c, current)))
            .ToList();
    }
}
