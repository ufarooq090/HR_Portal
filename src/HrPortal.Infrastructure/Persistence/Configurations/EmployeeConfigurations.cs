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
        builder.Property(e => e.JobTitle).HasMaxLength(200);
        builder.Property(e => e.CountryCode).HasMaxLength(2).IsFixedLength();

        // A rehire gets a new employee record on the same Person, so PersonId is not unique.
        builder.HasOne(e => e.Person).WithMany().HasForeignKey(e => e.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CountrySettings>().WithMany().HasForeignKey(e => e.CountryCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.LegalEntity).WithMany().HasForeignKey(e => e.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.OfficeLocation).WithMany().HasForeignKey(e => e.OfficeLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Department).WithMany().HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Grade).WithMany().HasForeignKey(e => e.GradeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Manager).WithMany().HasForeignKey(e => e.ManagerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.EmployeeNumber).IsUnique();
        builder.HasIndex(e => e.WorkEmail).IsUnique();
        builder.HasIndex(e => e.UserObjectId).IsUnique();
        builder.HasIndex(e => e.ManagerId);
        builder.HasIndex(e => e.CountryCode);
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
