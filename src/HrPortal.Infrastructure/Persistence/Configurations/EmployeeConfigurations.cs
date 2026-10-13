using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;
using HrPortal.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HrPortal.Infrastructure.Persistence.Configurations;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People");
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.GivenName).HasMaxLength(100);
        builder.Property(p => p.FamilyName).HasMaxLength(100);
        builder.Property(p => p.PersonalEmail).HasMaxLength(256);
        builder.Property(p => p.Phone).HasMaxLength(32);
        builder.Property(p => p.AddressLine1).HasMaxLength(200);
        builder.Property(p => p.AddressLine2).HasMaxLength(200);
        builder.Property(p => p.City).HasMaxLength(100);
        builder.Property(p => p.Region).HasMaxLength(100);
        builder.Property(p => p.PostalCode).HasMaxLength(20);
        builder.Property(p => p.AddressCountryCode).HasMaxLength(2).IsFixedLength();

        // Ciphertext is longer than the plaintext; no length limit.
        builder.Property(p => p.NationalId);

        builder.HasMany(p => p.EmergencyContacts).WithOne().HasForeignKey(c => c.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.EmergencyContacts).HasField("_emergencyContacts");
    }
}

internal sealed class EmergencyContactConfiguration : IEntityTypeConfiguration<EmergencyContact>
{
    public void Configure(EntityTypeBuilder<EmergencyContact> builder)
    {
        builder.ToTable("EmergencyContacts");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(200);
        builder.Property(c => c.Relationship).HasMaxLength(50);
        builder.Property(c => c.Phone).HasMaxLength(32);
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmployeeNumber).HasMaxLength(32);
        builder.Property(e => e.WorkEmail).HasMaxLength(256);
        builder.Property(e => e.UserObjectId).HasMaxLength(128);
        builder.Property(e => e.Designation).HasMaxLength(200);
        builder.Property(e => e.EmploymentType).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.CountryCode).HasMaxLength(2).IsFixedLength();

        // A rehire gets a new employee record on the same Person, so PersonId is not unique.
        builder.HasOne(e => e.Person).WithMany().HasForeignKey(e => e.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CountrySettings>().WithMany().HasForeignKey(e => e.CountryCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.LegalEntity).WithMany().HasForeignKey(e => e.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.OfficeLocation).WithMany().HasForeignKey(e => e.OfficeLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Department).WithMany().HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Grade).WithMany().HasForeignKey(e => e.GradeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Manager).WithMany().HasForeignKey(e => e.ManagerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.History).WithOne().HasForeignKey(h => h.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(e => e.History).HasField("_history");

        builder.HasIndex(e => e.EmployeeNumber).IsUnique();
        builder.HasIndex(e => e.WorkEmail).IsUnique();
        builder.HasIndex(e => e.UserObjectId).IsUnique();
        builder.HasIndex(e => e.ManagerId);
        builder.HasIndex(e => e.CountryCode);
        builder.HasIndex(e => e.Status);
    }
}

internal sealed class JobHistoryEntryConfiguration : IEntityTypeConfiguration<JobHistoryEntry>
{
    public void Configure(EntityTypeBuilder<JobHistoryEntry> builder)
    {
        builder.ToTable("JobHistory");
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(h => h.Designation).HasMaxLength(200);
        builder.Property(h => h.Reason).HasMaxLength(500);

        // Snapshots keep org items alive: anything referenced by history must be deactivated, not deleted.
        builder.HasOne<Department>().WithMany().HasForeignKey(h => h.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OfficeLocation>().WithMany().HasForeignKey(h => h.OfficeLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LegalEntity>().WithMany().HasForeignKey(h => h.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Grade>().WithMany().HasForeignKey(h => h.GradeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(h => h.ManagerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(h => new { h.EmployeeId, h.EffectiveDate });
    }
}

internal sealed class CompensationConfiguration : IEntityTypeConfiguration<Compensation>
{
    public void Configure(EntityTypeBuilder<Compensation> builder)
    {
        builder.ToTable("Compensation");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.CurrencyCode).HasMaxLength(3).IsFixedLength();
        builder.Property(c => c.Reason).HasConversion<string>().HasMaxLength(16);
        builder.HasOne<Employee>().WithMany().HasForeignKey(c => c.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.EmployeeId, c.EffectiveDate });
    }
}

internal sealed class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> builder)
    {
        builder.ToTable("EmployeeDocuments");
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Category).HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.FileName).HasMaxLength(200);
        builder.Property(d => d.ContentType).HasMaxLength(100);
        builder.Property(d => d.StorageKey).HasMaxLength(100);
        builder.HasOne<Employee>().WithMany().HasForeignKey(d => d.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.EmployeeId);
    }
}

internal sealed class HrAccessSettingsConfiguration : IEntityTypeConfiguration<HrAccessSettings>
{
    public void Configure(EntityTypeBuilder<HrAccessSettings> builder)
    {
        builder.ToTable("HrAccessSettings");
        builder.HasKey(s => s.EmployeeId);
        builder.Property(s => s.CountryCodes).HasColumnType("text[]");
        builder.HasOne<Employee>().WithOne().HasForeignKey<HrAccessSettings>(s => s.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ScheduledJobChangeConfiguration : IEntityTypeConfiguration<ScheduledJobChange>
{
    public void Configure(EntityTypeBuilder<ScheduledJobChange> builder)
    {
        builder.ToTable("ScheduledJobChanges");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.Designation).HasMaxLength(200);
        builder.Property(c => c.Reason).HasMaxLength(500);
        builder.Property(c => c.Outcome).HasMaxLength(500);
        builder.HasOne<Employee>().WithMany().HasForeignKey(c => c.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(c => c.NewManagerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LegalEntity>().WithMany().HasForeignKey(c => c.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OfficeLocation>().WithMany().HasForeignKey(c => c.OfficeLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(c => c.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Grade>().WithMany().HasForeignKey(c => c.GradeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.Status, c.EffectiveDate });
        builder.HasIndex(c => c.EmployeeId);
    }
}
