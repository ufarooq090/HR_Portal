using HrPortal.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HrPortal.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();
        builder.Property(a => a.ActorId).HasMaxLength(128).IsRequired();
        builder.Property(a => a.ActorName).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(128).IsRequired();
        builder.Property(a => a.OldValues).HasColumnType("jsonb");
        builder.Property(a => a.NewValues).HasColumnType("jsonb");
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.OccurredAtUtc);
        builder.HasIndex(a => a.ActorId);
    }
}
