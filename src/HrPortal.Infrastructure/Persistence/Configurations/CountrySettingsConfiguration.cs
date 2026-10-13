using HrPortal.Domain.Organisation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HrPortal.Infrastructure.Persistence.Configurations;

internal sealed class CountrySettingsConfiguration : IEntityTypeConfiguration<CountrySettings>
{
    public void Configure(EntityTypeBuilder<CountrySettings> builder)
    {
        builder.ToTable("CountrySettings");
        builder.HasKey(c => c.CountryCode);
        builder.Property(c => c.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(c => c.DefaultTimeZone).HasMaxLength(64).IsRequired();

        // Time zones are starting values for HR to confirm; the US zone depends on the state(s) of employment.
        builder.HasData(
            new CountrySettings("PK", "Pakistan", "PKR", "Asia/Karachi"),
            new CountrySettings("US", "United States", "USD", "America/New_York"));
    }
}
