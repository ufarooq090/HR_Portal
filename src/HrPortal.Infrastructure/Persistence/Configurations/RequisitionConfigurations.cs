using System.Text.Json;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Notifications;
using HrPortal.Domain.Organisation;
using HrPortal.Domain.Requisitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HrPortal.Infrastructure.Persistence.Configurations;

internal sealed class RequisitionConfiguration : IEntityTypeConfiguration<Requisition>
{
    public const string NumberSequence = "requisition_numbers";

    public void Configure(EntityTypeBuilder<Requisition> builder)
    {
        builder.ToTable("Requisitions");
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Number).HasMaxLength(20);
        builder.Property(r => r.Title).HasMaxLength(200);
        builder.Property(r => r.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(r => r.CurrencyCode).HasMaxLength(3).IsFixedLength();
        builder.Property(r => r.BudgetMin).HasPrecision(14, 2);
        builder.Property(r => r.BudgetMax).HasPrecision(14, 2);
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.JobDescription).HasMaxLength(Requisition.MaxJobDescriptionLength);

        builder.HasOne<Department>().WithMany().HasForeignKey(r => r.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OfficeLocation>().WithMany().HasForeignKey(r => r.OfficeLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CountrySettings>().WithMany().HasForeignKey(r => r.CountryCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(r => r.RequesterEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(r => r.ReplacedEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(r => r.HiringManagerEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.ScorecardCriteria).HasColumnType("text[]");
        builder.HasIndex(r => r.HiringManagerEmployeeId);

        builder.HasMany(r => r.Approvals).WithOne().HasForeignKey(a => a.RequisitionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Approvals).HasField("_approvals");
        builder.HasMany(r => r.StatusHistory).WithOne().HasForeignKey(s => s.RequisitionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.StatusHistory).HasField("_statusHistory");

        builder.HasIndex(r => r.Number).IsUnique();
        builder.HasIndex(r => new { r.CountryCode, r.Status });
        builder.HasIndex(r => r.DepartmentId);
        builder.HasIndex(r => r.RequesterEmployeeId);
    }
}

internal sealed class RequisitionApprovalConfiguration : IEntityTypeConfiguration<RequisitionApproval>
{
    public void Configure(EntityTypeBuilder<RequisitionApproval> builder)
    {
        builder.ToTable("RequisitionApprovals");
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Label).HasMaxLength(100);
        builder.Property(a => a.Decision).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Comment).HasMaxLength(1000);
        builder.HasOne<Employee>().WithMany().HasForeignKey(a => a.ApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.ApproverEmployeeId, a.Decision });
    }
}

internal sealed class RequisitionStatusChangeConfiguration : IEntityTypeConfiguration<RequisitionStatusChange>
{
    public void Configure(EntityTypeBuilder<RequisitionStatusChange> builder)
    {
        builder.ToTable("RequisitionStatusChanges");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.FromStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.ToStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.ActorId).HasMaxLength(128);
        builder.Property(s => s.ActorName).HasMaxLength(256);
        builder.Property(s => s.Comment).HasMaxLength(1000);
    }
}

internal sealed class ApprovalChainConfiguration : IEntityTypeConfiguration<ApprovalChain>
{
    public void Configure(EntityTypeBuilder<ApprovalChain> builder)
    {
        builder.ToTable("ApprovalChains");
        builder.HasKey(c => c.CountryCode);
        builder.Property(c => c.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.HasOne<CountrySettings>().WithMany().HasForeignKey(c => c.CountryCode).OnDelete(DeleteBehavior.Restrict);

        // One JSON column, so the audit trail shows the whole chain before and after a change.
        builder.Property(c => c.Steps)
            .HasColumnType("jsonb")
            .HasConversion(
                steps => JsonSerializer.Serialize(steps, JsonSerializerOptions.Default),
                json => JsonSerializer.Deserialize<List<ApprovalStep>>(json, JsonSerializerOptions.Default) ?? new List<ApprovalStep>(),
                new ValueComparer<List<ApprovalStep>>(
                    (a, b) => a!.SequenceEqual(b!),
                    steps => steps.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                    steps => steps.ToList()));
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Title).HasMaxLength(200);
        builder.Property(n => n.Body).HasMaxLength(2000);
        builder.Property(n => n.Link).HasMaxLength(300);
        builder.HasOne<Employee>().WithMany().HasForeignKey(n => n.RecipientEmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(n => new { n.RecipientEmployeeId, n.CreatedAtUtc });
    }
}
