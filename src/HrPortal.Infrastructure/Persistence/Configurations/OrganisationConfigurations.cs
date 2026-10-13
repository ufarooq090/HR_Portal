using HrPortal.Domain.Organisation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HrPortal.Infrastructure.Persistence.Configurations;

internal sealed class LegalEntityConfiguration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> builder)
    {
        builder.ToTable("LegalEntities");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.HasOne<CountrySettings>().WithMany().HasForeignKey(e => e.CountryCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CountryCode, e.Name }).IsUnique();
    }
}

internal sealed class OfficeLocationConfiguration : IEntityTypeConfiguration<OfficeLocation>
{
    public void Configure(EntityTypeBuilder<OfficeLocation> builder)
    {
        builder.ToTable("OfficeLocations");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.TimeZone).HasMaxLength(64);
        builder.HasOne<CountrySettings>().WithMany().HasForeignKey(e => e.CountryCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CountryCode, e.Name }).IsUnique();
    }
}

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Code).HasMaxLength(20);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.HasIndex(e => e.Code).IsUnique();
    }
}

internal sealed class GradeConfiguration : IEntityTypeConfiguration<Grade>
{
    public void Configure(EntityTypeBuilder<Grade> builder)
    {
        builder.ToTable("Grades");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(e => e.Code).HasMaxLength(20);
        builder.Property(e => e.Name).HasMaxLength(100);
        builder.HasOne<CountrySettings>().WithMany().HasForeignKey(e => e.CountryCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CountryCode, e.Code }).IsUnique();
    }
}
