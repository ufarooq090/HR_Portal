using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;

namespace HrPortal.UnitTests.Recruiting;

public class ResumeBankTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly Actor Hr = new("hr", "HR", null);

    private static CandidateProfile NewProfile() => new(Guid.NewGuid(), "PK", Now);

    private static UserAccess User(string[] roles, string country = "PK") => new(roles.ToHashSet(), Guid.NewGuid(), Guid.NewGuid(), country, null);

    [Theory]
    [InlineData(PipelineStage.Applied, null)]
    [InlineData(PipelineStage.Screening, ResumeBankStatus.Shortlisted)]
    [InlineData(PipelineStage.Interview, ResumeBankStatus.Shortlisted)]
    [InlineData(PipelineStage.OfferAccepted, ResumeBankStatus.Shortlisted)]
    [InlineData(PipelineStage.Rejected, ResumeBankStatus.NotShortlisted)]
    [InlineData(PipelineStage.NoShow, ResumeBankStatus.NoShow)]
    [InlineData(PipelineStage.OfferDeclined, ResumeBankStatus.OfferDeclined)]
    [InlineData(PipelineStage.Joined, ResumeBankStatus.Hired)]
    [InlineData(PipelineStage.Withdrawn, null)]
    [InlineData(PipelineStage.OnHold, null)]
    public void RB_03_pipeline_stages_set_the_resume_bank_status(PipelineStage stage, ResumeBankStatus? expected)
    {
        Assert.Equal(expected, ResumeBankStatusRules.FromPipeline(stage));
    }

    [Theory]
    [InlineData(EmploymentStatus.Resigned, ResumeBankStatus.Resigned)]
    [InlineData(EmploymentStatus.Terminated, ResumeBankStatus.Terminated)]
    [InlineData(EmploymentStatus.Absconded, ResumeBankStatus.Absconded)]
    [InlineData(EmploymentStatus.Confirmed, null)]
    public void RB_03_employee_exits_set_the_resume_bank_status(EmploymentStatus status, ResumeBankStatus? expected)
    {
        Assert.Equal(expected, ResumeBankStatusRules.FromEmployeeExit(status));
    }

    [Fact]
    public void RB_03_automatic_changes_are_logged_and_never_override_a_blacklist()
    {
        var profile = NewProfile();

        profile.ApplyAutomaticStatus(ResumeBankStatus.Shortlisted, StatusChangeSource.Pipeline, "REQ-1: Screening", Hr, Now);
        profile.ApplyAutomaticStatus(ResumeBankStatus.Shortlisted, StatusChangeSource.Pipeline, "REQ-1: Interview", Hr, Now);
        profile.ApplyAutomaticStatus(null, StatusChangeSource.Pipeline, "REQ-1: On hold", Hr, Now);
        profile.Blacklist("Forged documents", Hr, Now);
        profile.ApplyAutomaticStatus(ResumeBankStatus.Hired, StatusChangeSource.Pipeline, "REQ-1: Joined", Hr, Now);

        Assert.Equal(ResumeBankStatus.Blacklisted, profile.Status);
        Assert.Equal(
            [(null, ResumeBankStatus.Shortlisted, StatusChangeSource.Pipeline), (ResumeBankStatus.Shortlisted, ResumeBankStatus.Blacklisted, StatusChangeSource.Manual)],
            profile.StatusHistory.Select(h => (h.FromStatus, h.ToStatus, h.Source)));
    }

    [Fact]
    public void RB_03_blacklisting_needs_a_reason_and_cannot_be_done_as_a_plain_status()
    {
        var profile = NewProfile();

        Assert.Throws<DomainValidationException>(() => profile.Blacklist("  ", Hr, Now));
        Assert.Throws<DomainValidationException>(() => profile.SetStatus(ResumeBankStatus.Blacklisted, null, Hr, Now));

        profile.Blacklist("Forged documents", Hr, Now);
        Assert.Equal("Forged documents", profile.BlacklistReason);
        Assert.Throws<DomainValidationException>(() => profile.SetStatus(ResumeBankStatus.Shortlisted, null, Hr, Now));
        Assert.Throws<DomainValidationException>(() => profile.RemoveFromBlacklist("", Hr, Now));

        profile.RemoveFromBlacklist("Documents verified after all", Hr, Now);
        Assert.Equal((null, null), (profile.Status, profile.BlacklistReason));
        Assert.Equal("Documents verified after all", profile.StatusHistory[^1].Reason);
    }

    [Fact]
    public void RB_04_skills_and_tags_are_lower_case_and_distinct()
    {
        var profile = NewProfile();

        profile.UpdateResumeDetails("us", " New York ", 4.5m, ["C#", "c#", " React ", ""], ["Future Data Roles"]);

        Assert.Equal(("US", "New York", 4.5m), (profile.CountryCode, profile.Location, profile.ExperienceYears!.Value));
        Assert.Equal(["c#", "react"], profile.Skills);
        Assert.Equal(["future data roles"], profile.Tags);
        Assert.Throws<DomainValidationException>(() => profile.UpdateResumeDetails("PK", null, 4.55m, [], []));
        Assert.Throws<DomainValidationException>(() => profile.UpdateResumeDetails("PK", null, null, Enumerable.Range(0, 51).Select(i => $"s{i}"), []));
    }

    [Fact]
    public void RB_04_last_contacted_only_moves_forward()
    {
        var profile = NewProfile();

        profile.MarkContacted(Now);
        profile.MarkContacted(Now.AddDays(-3));

        Assert.Equal(Now, profile.LastContactedAtUtc);
    }

    [Fact]
    public void RB_scoping_hr_and_hiring_managers_by_country_hiring_managers_without_contact_salary_or_notes()
    {
        var pkExecutive = ResumeBankRules.For(User([Roles.HrExecutive]), "PK", false);
        var usExecutiveOnPk = ResumeBankRules.For(User([Roles.HrExecutive], "US"), "PK", false);
        var hrAdmin = ResumeBankRules.For(User([Roles.HrAdmin], "US"), "PK", false);
        var hiringManager = ResumeBankRules.For(User([Roles.HiringManager], "US"), "US", false);
        var otherCountryManager = ResumeBankRules.For(User([Roles.HiringManager], "PK"), "US", false);
        var ownHiringManager = ResumeBankRules.For(User([Roles.HiringManager], "PK"), "US", true);

        Assert.Equal(new ResumeBankPermissions(true, true, true, true, true, false), pkExecutive);
        Assert.False(usExecutiveOnPk.CanView);
        Assert.Equal(new ResumeBankPermissions(true, true, true, true, true, true), hrAdmin);
        Assert.Equal(new ResumeBankPermissions(true, false, false, false, false, false), hiringManager);
        Assert.False(otherCountryManager.CanView);

        // A candidate on the manager's own requisition stays visible to them, as in the Candidate Tracker.
        Assert.True(ownHiringManager.CanView);
        Assert.True(ownHiringManager.CanSeeCompensation);
        Assert.False(ownHiringManager.CanSeeNotes);
        Assert.False(ResumeBankRules.For(User([Roles.LineManager, Roles.Interviewer, Roles.Employee]), "PK", false).CanView);
    }
}
