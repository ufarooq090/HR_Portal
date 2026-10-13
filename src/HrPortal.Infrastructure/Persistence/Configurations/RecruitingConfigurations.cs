using System.Text.Json;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace HrPortal.Infrastructure.Persistence.Configurations;

internal sealed class CandidateProfileConfiguration : IEntityTypeConfiguration<CandidateProfile>
{
    public void Configure(EntityTypeBuilder<CandidateProfile> builder)
    {
        builder.ToTable("CandidateProfiles");
        builder.HasKey(c => c.PersonId);
        builder.Property(c => c.CurrentEmployer).HasMaxLength(200);
        builder.Property(c => c.CurrentCtcCurrency).HasMaxLength(3).IsFixedLength();
        builder.Property(c => c.ExpectedCtcCurrency).HasMaxLength(3).IsFixedLength();
        builder.HasOne<Person>().WithOne().HasForeignKey<CandidateProfile>(c => c.PersonId).OnDelete(DeleteBehavior.Restrict);

        // Resume Bank (RB-02..06).
        builder.Property(c => c.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.HasOne<CountrySettings>().WithMany().HasForeignKey(c => c.CountryCode).OnDelete(DeleteBehavior.Restrict);
        builder.Property(c => c.Location).HasMaxLength(100);
        builder.Property(c => c.ExperienceYears).HasPrecision(4, 1);
        builder.Property(c => c.Skills).HasColumnType("text[]");
        builder.Property(c => c.Tags).HasColumnType("text[]");
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.BlacklistReason).HasMaxLength(1000);
        builder.HasMany(c => c.StatusHistory).WithOne().HasForeignKey(h => h.PersonId).HasPrincipalKey(c => c.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.StatusHistory).HasField("_statusHistory");
        builder.HasIndex(c => c.Skills).HasMethod("GIN");
        builder.HasIndex(c => c.Tags).HasMethod("GIN");
        builder.HasIndex(c => new { c.CountryCode, c.Status });
        builder.HasIndex(c => c.LastContactedAtUtc);
    }
}

internal sealed class ResumeBankStatusChangeConfiguration : IEntityTypeConfiguration<ResumeBankStatusChange>
{
    public void Configure(EntityTypeBuilder<ResumeBankStatusChange> builder)
    {
        builder.ToTable("ResumeBankStatusChanges");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.FromStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.ToStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Reason).HasMaxLength(1000);
        builder.Property(s => s.ActorId).HasMaxLength(128);
        builder.Property(s => s.ActorName).HasMaxLength(256);
        builder.HasIndex(s => s.PersonId);
    }
}

internal sealed class CandidateNoteConfiguration : IEntityTypeConfiguration<CandidateNote>
{
    public void Configure(EntityTypeBuilder<CandidateNote> builder)
    {
        builder.ToTable("CandidateNotes");
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Body).HasMaxLength(4000);
        builder.Property(n => n.AuthorId).HasMaxLength(128);
        builder.Property(n => n.AuthorName).HasMaxLength(256);
        builder.HasOne<Person>().WithMany().HasForeignKey(n => n.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(n => n.PersonId);
    }
}

internal sealed class CandidateCvConfiguration : IEntityTypeConfiguration<CandidateCv>
{
    public void Configure(EntityTypeBuilder<CandidateCv> builder)
    {
        builder.ToTable("CandidateCvs");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.FileName).HasMaxLength(200);
        builder.Property(c => c.ContentType).HasMaxLength(100);
        builder.Property(c => c.StorageKey).HasMaxLength(100);
        builder.Property(c => c.UploadedBy).HasMaxLength(256);
        builder.HasOne<Person>().WithMany().HasForeignKey(c => c.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.PersonId);

        // RB-04: full-text search inside CV content. English stemming, so "developing" finds "developer".
        builder.Property<NpgsqlTsVector>(SearchVector)
            .HasComputedColumnSql("to_tsvector('english', coalesce(\"ExtractedText\", ''))", stored: true);
        builder.HasIndex(SearchVector).HasMethod("GIN");
    }

    /// <summary>Shadow column, so the domain stays free of Npgsql types.</summary>
    public const string SearchVector = "SearchVector";
}

internal sealed class RejectionReasonConfiguration : IEntityTypeConfiguration<RejectionReason>
{
    public void Configure(EntityTypeBuilder<RejectionReason> builder)
    {
        builder.ToTable("RejectionReasons");
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(100);
        builder.HasIndex(r => r.Name).IsUnique();

        // A starting list for HR to edit (CT-03).
        builder.HasData(
            new RejectionReason(new Guid("0199c3a0-0000-7000-8000-000000000001"), "Skills do not match the role", 10),
            new RejectionReason(new Guid("0199c3a0-0000-7000-8000-000000000002"), "Not enough experience", 20),
            new RejectionReason(new Guid("0199c3a0-0000-7000-8000-000000000003"), "Salary expectations too high", 30),
            new RejectionReason(new Guid("0199c3a0-0000-7000-8000-000000000004"), "Did not pass the interview", 40),
            new RejectionReason(new Guid("0199c3a0-0000-7000-8000-000000000005"), "Notice period too long", 50),
            new RejectionReason(new Guid("0199c3a0-0000-7000-8000-000000000006"), "Position filled", 60),
            new RejectionReason(new Guid("0199c3a0-0000-7000-8000-000000000007"), "Other", 100));
    }
}

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.ToTable("Applications");
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Stage).HasConversion<string>().HasMaxLength(16);
        builder.HasOne<Person>().WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Requisition>().WithMany().HasForeignKey(a => a.RequisitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(a => a.History).WithOne().HasForeignKey(h => h.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.History).HasField("_history");

        // One application per person per requisition.
        builder.HasIndex(a => new { a.RequisitionId, a.PersonId }).IsUnique();
        builder.HasIndex(a => a.PersonId);
    }
}

internal sealed class ApplicationStageChangeConfiguration : IEntityTypeConfiguration<ApplicationStageChange>
{
    public void Configure(EntityTypeBuilder<ApplicationStageChange> builder)
    {
        builder.ToTable("ApplicationStageChanges");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.FromStage).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.ToStage).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.RejectionReasonName).HasMaxLength(100);
        builder.Property(s => s.Reason).HasMaxLength(1000);
        builder.Property(s => s.ActorId).HasMaxLength(128);
        builder.Property(s => s.ActorName).HasMaxLength(256);
        builder.HasOne<RejectionReason>().WithMany().HasForeignKey(s => s.RejectionReasonId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ApplicationNoteConfiguration : IEntityTypeConfiguration<ApplicationNote>
{
    public void Configure(EntityTypeBuilder<ApplicationNote> builder)
    {
        builder.ToTable("ApplicationNotes");
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Body).HasMaxLength(4000);
        builder.Property(n => n.AuthorId).HasMaxLength(128);
        builder.Property(n => n.AuthorName).HasMaxLength(256);
        builder.HasOne<Application>().WithMany().HasForeignKey(n => n.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(n => n.ApplicationId);
    }
}

internal sealed class InterviewConfiguration : IEntityTypeConfiguration<Interview>
{
    public void Configure(EntityTypeBuilder<Interview> builder)
    {
        builder.ToTable("Interviews");
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Mode).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.Location).HasMaxLength(500);
        builder.Property(i => i.CalendarEventId).HasMaxLength(256);
        builder.Property(i => i.OrganizerEmail).HasMaxLength(256);
        builder.Property(i => i.Criteria).HasColumnType("text[]");
        builder.HasOne<Application>().WithMany().HasForeignKey(i => i.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(i => i.Panel).WithOne().HasForeignKey(p => p.InterviewId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Panel).HasField("_panel");
        builder.HasIndex(i => i.ApplicationId);
    }
}

internal sealed class InterviewPanelistConfiguration : IEntityTypeConfiguration<InterviewPanelist>
{
    public void Configure(EntityTypeBuilder<InterviewPanelist> builder)
    {
        builder.ToTable("InterviewPanelists");
        builder.HasKey(p => new { p.InterviewId, p.EmployeeId });
        builder.HasOne<Employee>().WithMany().HasForeignKey(p => p.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.EmployeeId);
    }
}

internal sealed class ScorecardConfiguration : IEntityTypeConfiguration<Scorecard>
{
    public void Configure(EntityTypeBuilder<Scorecard> builder)
    {
        builder.ToTable("Scorecards");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Recommendation).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Comment).HasMaxLength(4000);
        builder.Property(s => s.Ratings)
            .HasColumnType("jsonb")
            .HasConversion(
                ratings => JsonSerializer.Serialize(ratings, JsonSerializerOptions.Default),
                json => JsonSerializer.Deserialize<List<CriterionRating>>(json, JsonSerializerOptions.Default) ?? new List<CriterionRating>(),
                new ValueComparer<List<CriterionRating>>(
                    (a, b) => a!.SequenceEqual(b!),
                    ratings => ratings.Aggregate(0, (h, r) => HashCode.Combine(h, r.GetHashCode())),
                    ratings => ratings.ToList()));
        builder.HasOne<Interview>().WithMany().HasForeignKey(s => s.InterviewId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(s => s.InterviewerEmployeeId).OnDelete(DeleteBehavior.Restrict);

        // One scorecard per panelist per interview.
        builder.HasIndex(s => new { s.InterviewId, s.InterviewerEmployeeId }).IsUnique();
    }
}
