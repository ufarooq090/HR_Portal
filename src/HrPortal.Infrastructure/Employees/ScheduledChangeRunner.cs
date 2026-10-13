using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HrPortal.Infrastructure.Employees;

/// <summary>
/// Applies scheduled job and manager changes once their effective date has started in the employee's office time
/// zone. Run hourly by Hangfire. Each change is saved on its own, as the "system" actor, so it is audited and one
/// failure does not hold up the rest. A change that can no longer be applied (reporting loop, employee left,
/// inactive office) is marked Failed with the reason, for HR to see on the employee record.
/// </summary>
public sealed class ScheduledChangeRunner(HrPortalDbContext db, TimeProvider clock, ILogger<ScheduledChangeRunner> logger)
{
    public const string RecurringJobId = "apply-scheduled-job-changes";

    public Task<int> ApplyDueAsync(CancellationToken cancellationToken) => ApplyDueAsync(clock.GetUtcNow(), cancellationToken);

    /// <returns>How many changes were applied.</returns>
    public async Task<int> ApplyDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // No time zone is more than a day ahead of UTC, so nothing due can be later than tomorrow (UTC).
        var horizon = DateOnly.FromDateTime(now.UtcDateTime).AddDays(1);
        var candidates = await (
                from c in db.ScheduledJobChanges
                where c.Status == ScheduledChangeStatus.Pending && c.EffectiveDate <= horizon
                join e in db.Employees on c.EmployeeId equals e.Id
                join o in db.OfficeLocations on e.OfficeLocationId equals o.Id
                join k in db.CountrySettings on o.CountryCode equals k.CountryCode
                orderby c.EffectiveDate, c.Id
                select new { c.Id, c.EffectiveDate, TimeZone = o.TimeZone ?? k.DefaultTimeZone })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var applied = 0;
        foreach (var candidate in candidates)
        {
            var today = LocalCalendar.Today(now, candidate.TimeZone);
            if (candidate.EffectiveDate > today)
            {
                continue;
            }

            db.ChangeTracker.Clear();
            var change = await db.ScheduledJobChanges.SingleAsync(c => c.Id == candidate.Id, cancellationToken);
            try
            {
                await ApplyAsync(change, today, cancellationToken);
                change.MarkApplied(now);
                await db.SaveChangesAsync(cancellationToken);
                applied++;
            }
            catch (DomainValidationException e)
            {
                logger.LogWarning("Scheduled change {ChangeId} could not be applied: {Reason}", change.Id, e.Message);
                db.ChangeTracker.Clear();
                var failed = await db.ScheduledJobChanges.SingleAsync(c => c.Id == candidate.Id, cancellationToken);
                failed.MarkFailed(e.Message, now);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        db.ChangeTracker.Clear();
        return applied;
    }

    private async Task ApplyAsync(ScheduledJobChange change, DateOnly today, CancellationToken ct)
    {
        var employee = await db.Employees.SingleAsync(e => e.Id == change.EmployeeId, ct);

        if (change.IsManagerChange)
        {
            var manager = change.NewManagerId is { } managerId ? await db.Employees.SingleAsync(e => e.Id == managerId, ct) : null;

            // The reporting line may have changed since scheduling, so the loop check runs again here.
            var loop = manager is not null && await db.ReportIds(employee.Id).AnyAsync(r => r == manager.Id, ct);
            employee.ChangeManager(manager, loop, change.EffectiveDate, change.Reason, today);
            return;
        }

        var legalEntity = await db.LegalEntities.SingleAsync(x => x.Id == change.LegalEntityId, ct);
        var office = await db.OfficeLocations.SingleAsync(x => x.Id == change.OfficeLocationId, ct);
        var department = await db.Departments.SingleAsync(x => x.Id == change.DepartmentId, ct);
        var grade = change.GradeId is { } gradeId ? await db.Grades.SingleAsync(x => x.Id == gradeId, ct) : null;
        if (!legalEntity.IsActive || !office.IsActive || !department.IsActive || grade is { IsActive: false })
        {
            throw new DomainValidationException("status", "The legal entity, office, department or grade has been deactivated since the change was scheduled.");
        }

        employee.ChangeJob(change.Kind, new JobPlacement(change.Designation!, legalEntity, office, department, grade), change.EffectiveDate, change.Reason, today);
    }
}
