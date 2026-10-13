using System.Reflection;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Notifications;
using HrPortal.Domain.Organisation;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Infrastructure.Persistence;

public sealed class HrPortalDbContext(DbContextOptions<HrPortalDbContext> options, IFieldEncryptor encryptor)
    : DbContext(options)
{
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<CountrySettings> CountrySettings => Set<CountrySettings>();

    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();

    public DbSet<OfficeLocation> OfficeLocations => Set<OfficeLocation>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Grade> Grades => Set<Grade>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<HrAccessSettings> HrAccessSettings => Set<HrAccessSettings>();

    public DbSet<JobHistoryEntry> JobHistory => Set<JobHistoryEntry>();

    public DbSet<Compensation> Compensation => Set<Compensation>();

    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();

    public DbSet<ScheduledJobChange> ScheduledJobChanges => Set<ScheduledJobChange>();

    public DbSet<Requisition> Requisitions => Set<Requisition>();

    public DbSet<RequisitionApproval> RequisitionApprovals => Set<RequisitionApproval>();

    public DbSet<ApprovalChain> ApprovalChains => Set<ApprovalChain>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();

    public DbSet<CandidateCv> CandidateCvs => Set<CandidateCv>();

    public DbSet<RejectionReason> RejectionReasons => Set<RejectionReason>();

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<ApplicationNote> ApplicationNotes => Set<ApplicationNote>();

    public DbSet<Interview> Interviews => Set<Interview>();

    public DbSet<Scorecard> Scorecards => Set<Scorecard>();

    public DbSet<CandidateNote> CandidateNotes => Set<CandidateNote>();

    public DbSet<ResumeBankStatusChange> ResumeBankStatusChanges => Set<ResumeBankStatusChange>();

    /// <summary>Next requisition reference, e.g. REQ-00042, from a database sequence (no gaps in normal use, never reused).</summary>
    public async Task<string> NextRequisitionNumberAsync(CancellationToken ct)
    {
        var next = await Database
            .SqlQueryRaw<long>($"""SELECT nextval('{Configurations.RequisitionConfiguration.NumberSequence}') AS "Value" """)
            .SingleAsync(ct);
        return $"REQ-{next:D5}";
    }

    /// <summary>
    /// Ids of everyone who reports to <paramref name="managerId"/>, directly or indirectly. Composable: use it
    /// inside other queries so team scoping runs in the database (function created by a migration).
    /// </summary>
    public IQueryable<Guid> ReportIds(Guid managerId) =>
        Database.SqlQuery<Guid>($"""SELECT "Value" FROM employee_report_ids({managerId})""");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(Configurations.RequisitionConfiguration.NumberSequence);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HrPortalDbContext).Assembly);
        ApplySensitiveFieldEncryption(modelBuilder);
    }

    /// <summary>Every property marked [Sensitive] is encrypted at rest.</summary>
    /// <remarks>The model is cached per process, so the encryptor (a singleton) is captured once.</remarks>
    private void ApplySensitiveFieldEncryption(ModelBuilder modelBuilder)
    {
        var converter = new EncryptedStringConverter(encryptor);
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            if (property.PropertyInfo?.GetCustomAttribute<SensitiveAttribute>() is null)
            {
                continue;
            }

            if (property.ClrType != typeof(string))
            {
                throw new NotSupportedException(
                    $"[Sensitive] is only supported on string properties ({property.DeclaringType.Name}.{property.Name}).");
            }

            property.SetValueConverter(converter);
        }
    }
}
