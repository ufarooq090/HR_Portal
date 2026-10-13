using System.Reflection;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;
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

    /// <summary>
    /// Ids of everyone who reports to <paramref name="managerId"/>, directly or indirectly. Composable: use it
    /// inside other queries so team scoping runs in the database (function created by a migration).
    /// </summary>
    public IQueryable<Guid> ReportIds(Guid managerId) =>
        Database.SqlQuery<Guid>($"""SELECT "Value" FROM employee_report_ids({managerId})""");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
