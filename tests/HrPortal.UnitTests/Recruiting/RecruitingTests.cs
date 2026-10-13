using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;

namespace HrPortal.UnitTests.Recruiting;

public class RecruitingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly Actor Hr = new("hr", "HR", null);
    private static readonly CountrySettings Pakistan = new("PK", "Pakistan", "PKR", "Asia/Karachi");
    private static readonly OfficeLocation Lahore = new("PK", "Lahore", null, null);
    private static readonly Department Engineering = new("ENG", "Engineering");
    private static readonly RejectionReason Skills = new("Skills", 1);

    private static Requisition OpenRequisition()
    {
        var requisition = Requisition.Create(
            "REQ-1",
            new RequisitionDetails("Engineer", Engineering, Lahore, 1, Money.Pkr(1), Money.Pkr(2), RequisitionReason.NewPosition, null, new DateOnly(2030, 1, 1), "x"),
            Pakistan, Guid.NewGuid(), Hr, Now);
        var ceo = Guid.NewGuid();
        requisition.Submit([new ApprovalStep("CEO", ceo)], Hr, new DateOnly(2026, 10, 13), Now);
        requisition.Decide(ceo, true, null, Hr, Now);
        return requisition;
    }

    private static Application NewApplication() => Application.Create(Guid.NewGuid(), OpenRequisition(), CandidateSource.Portal, Hr, Now);

    private static Employee NewEmployee() =>
        Employee.Create(new Person("A", "B"), Guid.NewGuid().ToString("N")[..8], $"{Guid.NewGuid():N}@x.test", EmploymentType.Permanent,
            new JobPlacement("Dev", new LegalEntity("PK", "PK Ltd"), Lahore, Engineering, null), null, new DateOnly(2025, 1, 1), new EmploymentTerms(0, 30));

    [Fact]
    public void REQ_03_applications_need_an_open_requisition()
    {
        var draft = Requisition.Create(
            "R", new RequisitionDetails("E", Engineering, Lahore, 1, Money.Pkr(1), Money.Pkr(2), RequisitionReason.NewPosition, null, new DateOnly(2030, 1, 1), "x"),
            Pakistan, Guid.NewGuid(), Hr, Now);

        Assert.Throws<DomainValidationException>(() => Application.Create(Guid.NewGuid(), draft, CandidateSource.Portal, Hr, Now));
    }

    [Fact]
    public void CT_03_every_stage_change_is_logged_with_reason()
    {
        var application = NewApplication();

        application.MoveTo(PipelineStage.Screening, null, null, "Good CV", 2, Hr, Now);
        application.MoveTo(PipelineStage.Interview, 2, null, null, 2, Hr, Now);
        application.MoveTo(PipelineStage.Rejected, null, Skills, "Weak SQL", 2, Hr, Now);

        Assert.Equal([PipelineStage.Applied, PipelineStage.Screening, PipelineStage.Interview, PipelineStage.Rejected], application.History.Select(h => h.ToStage));
        Assert.Equal(("Skills", "Weak SQL", 2), (application.History[^1].RejectionReasonName, application.History[^1].Reason, application.History[^1].FromRound!.Value));
    }

    [Theory]
    [InlineData(PipelineStage.Interview, null)]
    [InlineData(PipelineStage.Interview, 3)]
    [InlineData(PipelineStage.Screening, 1)]
    public void CT_01_interview_stage_needs_a_round_within_the_pipeline(PipelineStage stage, int? round)
    {
        Assert.Throws<DomainValidationException>(() => NewApplication().MoveTo(stage, round, null, null, 2, Hr, Now));
    }

    [Fact]
    public void CT_03_rejection_needs_an_active_reason_from_the_list()
    {
        var inactive = new RejectionReason("Old", 1);
        inactive.Update("Old", 1, isActive: false);

        Assert.Equal("rejectionReasonId", Assert.Throws<DomainValidationException>(() => NewApplication().MoveTo(PipelineStage.Rejected, null, null, null, 2, Hr, Now)).Field);
        Assert.Throws<DomainValidationException>(() => NewApplication().MoveTo(PipelineStage.Rejected, null, inactive, null, 2, Hr, Now));
        Assert.Throws<DomainValidationException>(() => NewApplication().MoveTo(PipelineStage.Withdrawn, null, Skills, null, 2, Hr, Now));
    }

    [Theory]
    [InlineData(PipelineStage.Screening, PipelineStage.OfferAccepted)]
    [InlineData(PipelineStage.Offer, PipelineStage.Joined)]
    [InlineData(PipelineStage.Screening, PipelineStage.OfferDeclined)]
    public void CT_03_offer_steps_follow_in_order(PipelineStage from, PipelineStage to)
    {
        var application = NewApplication();
        application.MoveTo(from, null, null, null, 2, Hr, Now);

        Assert.Throws<DomainValidationException>(() => application.MoveTo(to, null, null, null, 2, Hr, Now));
    }

    [Fact]
    public void CT_03_exits_can_be_reopened_but_joined_is_final()
    {
        var application = NewApplication();
        application.MoveTo(PipelineStage.OnHold, null, null, "Budget review", 2, Hr, Now);
        application.MoveTo(PipelineStage.Screening, null, null, "Back on", 2, Hr, Now);
        application.MoveTo(PipelineStage.Offer, null, null, null, 2, Hr, Now);
        application.MoveTo(PipelineStage.OfferAccepted, null, null, null, 2, Hr, Now);
        application.MoveTo(PipelineStage.Joined, null, null, null, 2, Hr, Now);

        Assert.Throws<DomainValidationException>(() => application.MoveTo(PipelineStage.Withdrawn, null, null, null, 2, Hr, Now));
        Assert.Throws<DomainValidationException>(() => NewApplication().MoveTo(PipelineStage.Applied, null, null, null, 2, Hr, Now));
    }

    [Fact]
    public void CT_04_interview_needs_a_current_panel_and_copies_the_criteria()
    {
        var application = NewApplication();
        var panelist = NewEmployee();

        var interview = Interview.Schedule(application, 1, 2, Now, 60, InterviewMode.Phone, null, [panelist], ["A", "B"]);

        Assert.Equal(["A", "B"], interview.Criteria);
        Assert.True(interview.HasPanelist(panelist.Id));
        Assert.Throws<DomainValidationException>(() => Interview.Schedule(application, 1, 2, Now, 60, InterviewMode.Phone, null, [], ["A"]));
        Assert.Throws<DomainValidationException>(() => Interview.Schedule(application, 3, 2, Now, 60, InterviewMode.Phone, null, [panelist], ["A"]));
        Assert.Throws<DomainValidationException>(() => Interview.Schedule(application, 1, 2, Now, 5, InterviewMode.Phone, null, [panelist], ["A"]));
    }

    [Fact]
    public void CT_06_scorecard_rates_each_criterion_once_from_1_to_5()
    {
        var panelist = NewEmployee();
        var interview = Interview.Schedule(NewApplication(), 1, 2, Now, 60, InterviewMode.Teams, null, [panelist], ["Coding", "Design"]);

        var scorecard = Scorecard.Submit(interview, panelist.Id, [new("design", 4, null), new("Coding", 5, "Great")], Recommendation.StrongHire, null, Now);

        Assert.Equal(["Coding", "Design"], scorecard.Ratings.Select(r => r.Criterion));
        Assert.Throws<DomainValidationException>(() => Scorecard.Submit(interview, panelist.Id, [new("Coding", 5, null)], Recommendation.Hire, null, Now));
        Assert.Throws<DomainValidationException>(() => Scorecard.Submit(interview, panelist.Id, [new("Coding", 0, null), new("Design", 3, null)], Recommendation.Hire, null, Now));
        Assert.Throws<InvalidOperationException>(() => Scorecard.Submit(interview, Guid.NewGuid(), [new("Coding", 3, null), new("Design", 3, null)], Recommendation.Hire, null, Now));
    }

    [Fact]
    public void CT_07_a_panelist_sees_others_feedback_only_after_submitting()
    {
        var (first, second) = (NewEmployee(), NewEmployee());
        var interview = Interview.Schedule(NewApplication(), 1, 2, Now, 60, InterviewMode.Teams, null, [first, second], ["A"]);
        var firstCard = Scorecard.Submit(interview, first.Id, [new("A", 3, null)], Recommendation.Hire, null, Now);

        Assert.Empty(RecruitingRules.VisibleScorecards(interview, [firstCard], second.Id, canSeeAllFeedback: false));
        Assert.Empty(RecruitingRules.VisibleScorecards(interview, [firstCard], second.Id, canSeeAllFeedback: true));
        Assert.Single(RecruitingRules.VisibleScorecards(interview, [firstCard], first.Id, canSeeAllFeedback: false));
        Assert.Single(RecruitingRules.VisibleScorecards(interview, [firstCard], Guid.NewGuid(), canSeeAllFeedback: true));
        Assert.Empty(RecruitingRules.VisibleScorecards(interview, [firstCard], Guid.NewGuid(), canSeeAllFeedback: false));
    }

    [Fact]
    public void Recruiting_permissions_follow_the_matrix()
    {
        var hiringManager = Guid.NewGuid();
        var facts = new ApplicationFacts("PK", Guid.NewGuid(), hiringManager, ViewerIsPanelist: false);
        UserAccess User(string[] roles, Guid? id = null, string country = "PK") => new(roles.ToHashSet(), id ?? Guid.NewGuid(), Guid.NewGuid(), country, null);

        Assert.Equal(new ApplicationPermissions(true, true, true, true, true), RecruitingRules.For(User([Roles.HrExecutive]), facts));
        Assert.Equal(new ApplicationPermissions(false, false, false, false, false), RecruitingRules.For(User([Roles.HrExecutive], country: "US"), facts));
        Assert.Equal(new ApplicationPermissions(true, false, true, true, true), RecruitingRules.For(User([Roles.HiringManager], hiringManager), facts));
        Assert.False(RecruitingRules.For(User([Roles.LineManager], hiringManager), facts).CanView);
        Assert.Equal(new ApplicationPermissions(true, false, false, false, false), RecruitingRules.For(User([Roles.Employee]), facts with { ViewerIsPanelist = true }));
    }

    [Fact]
    public void MC_02_candidate_salary_is_in_a_country_currency()
    {
        var profile = new CandidateProfile(Guid.NewGuid(), Now);
        var currencies = new HashSet<string> { "PKR", "USD" };

        profile.Update("Acme", 30, Money.Pkr(100), Money.Usd(200), currencies);

        Assert.Equal((Money.Pkr(100), Money.Usd(200)), (profile.CurrentCtc!.Value, profile.ExpectedCtc!.Value));
        Assert.Throws<DomainValidationException>(() => profile.Update(null, null, new Money(1, "EUR"), null, currencies));
        Assert.Throws<DomainValidationException>(() => profile.Update(null, 400, null, null, currencies));
    }

    [Theory]
    [InlineData("+92 300 1234567", "3001234567")]
    [InlineData("0300-1234567", "3001234567")]
    [InlineData("+1 (212) 555-0101", "2125550101")]
    [InlineData("12345", null)]
    [InlineData(null, null)]
    public void CT_09_phone_numbers_match_regardless_of_format(string? phone, string? key)
    {
        Assert.Equal(key, Person.PhoneKeyOf(phone));
    }

    [Fact]
    public void CT_01_pipeline_rounds_cannot_shrink_below_rounds_in_use()
    {
        var requisition = OpenRequisition();

        requisition.ConfigurePipeline(4, ["Coding", "Design"], roundsInUse: 2);

        Assert.Equal((4, 2), (requisition.InterviewRounds, requisition.ScorecardCriteria.Count));
        Assert.Throws<DomainValidationException>(() => requisition.ConfigurePipeline(1, ["Coding"], roundsInUse: 2));
        Assert.Throws<DomainValidationException>(() => requisition.ConfigurePipeline(2, ["Coding", "coding"], roundsInUse: 0));
    }
}
