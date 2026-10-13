using System.Text.Json;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;
using HrPortal.Domain.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HrPortal.Infrastructure.Persistence.Configurations;

internal sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("EmailTemplates");
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(150);
        builder.Property(t => t.Category).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.RecordType).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Subject).HasMaxLength(EmailTemplate.MaxSubjectLength);
        builder.Property(t => t.BodyHtml).HasMaxLength(EmailTemplate.MaxBodyLength);
        builder.Property(t => t.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.HasOne<CountrySettings>().WithMany().HasForeignKey(t => t.CountryCode).OnDelete(DeleteBehavior.Restrict);
        builder.Property(t => t.DefaultSender).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Trigger).HasMaxLength(300);
        builder.Property(t => t.StarterKey).HasMaxLength(60);
        builder.HasIndex(t => t.StarterKey).IsUnique().HasFilter("\"StarterKey\" IS NOT NULL");
        builder.HasIndex(t => t.Name).IsUnique();
        builder.Ignore(t => t.Current);
        builder.HasMany(t => t.Versions).WithOne().HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.Versions).HasField("_versions");
    }
}

internal sealed class EmailTemplateVersionConfiguration : IEntityTypeConfiguration<EmailTemplateVersion>
{
    public void Configure(EntityTypeBuilder<EmailTemplateVersion> builder)
    {
        builder.ToTable("EmailTemplateVersions");
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Name).HasMaxLength(150);
        builder.Property(v => v.Category).HasConversion<string>().HasMaxLength(16);
        builder.Property(v => v.Subject).HasMaxLength(EmailTemplate.MaxSubjectLength);
        builder.Property(v => v.BodyHtml).HasMaxLength(EmailTemplate.MaxBodyLength);
        builder.Property(v => v.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(v => v.DefaultSender).HasConversion<string>().HasMaxLength(16);
        builder.Property(v => v.ChangedById).HasMaxLength(128);
        builder.Property(v => v.ChangedByName).HasMaxLength(256);
        builder.HasIndex(v => new { v.TemplateId, v.Version }).IsUnique();
    }
}

internal sealed class EmailLogEntryConfiguration : IEntityTypeConfiguration<EmailLogEntry>
{
    public void Configure(EntityTypeBuilder<EmailLogEntry> builder)
    {
        builder.ToTable("EmailLog");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.HasOne<Person>().WithMany().HasForeignKey(e => e.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmailTemplate>().WithMany().HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.RecordType).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.TemplateName).HasMaxLength(150);
        builder.Property(e => e.Category).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.To).HasMaxLength(320);
        builder.Property(e => e.Cc).HasColumnType("text[]");
        builder.Property(e => e.Sender).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.FromAddress).HasMaxLength(320);
        builder.Property(e => e.Subject).HasMaxLength(EmailTemplate.MaxSubjectLength);
        builder.Property(e => e.SentById).HasMaxLength(128);
        builder.Property(e => e.SentByName).HasMaxLength(256);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Error).HasMaxLength(500);
        builder.Property(e => e.Attachments)
            .HasColumnType("jsonb")
            .HasConversion(
                list => JsonSerializer.Serialize(list, JsonSerializerOptions.Default),
                json => JsonSerializer.Deserialize<List<EmailLogAttachment>>(json, JsonSerializerOptions.Default) ?? new List<EmailLogAttachment>(),
                new ValueComparer<List<EmailLogAttachment>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode())),
                    list => list.ToList()));
        builder.HasIndex(e => new { e.PersonId, e.SentAtUtc });
        builder.HasIndex(e => e.EmployeeId);
        builder.HasIndex(e => e.BatchId);
    }
}

internal sealed class EmailAttachmentUploadConfiguration : IEntityTypeConfiguration<EmailAttachmentUpload>
{
    public void Configure(EntityTypeBuilder<EmailAttachmentUpload> builder)
    {
        builder.ToTable("EmailAttachmentUploads");
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.StorageKey).HasMaxLength(300);
        builder.Property(u => u.FileName).HasMaxLength(255);
        builder.Property(u => u.ContentType).HasMaxLength(100);
        builder.Property(u => u.UploadedById).HasMaxLength(128);
    }
}
