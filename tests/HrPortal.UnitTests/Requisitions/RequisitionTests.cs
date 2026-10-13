using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;

namespace HrPortal.UnitTests.Requisitions;

public class RequisitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly CountrySettings Pakistan = new("PK", "Pakistan", "PKR", "Asia/Karachi");
    private static readonly OfficeLocation Lahore = new("PK", "Lahore", null, null);
    private static readonly Department Engineering = new("ENG", "Engineering");
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Ceo = Guid.NewGuid();
    private static readonly Guid Cfo = Guid.NewGuid();
    private static readonly Actor Someone = new("user", "Someone", Requester);

    private static RequisitionDetails Details(decimal min = 100, decimal max = 200, string currency = "PKR") =>
        new("Engineer", Engineering, Lahore, 2, new Money(min, currency), new Money(max, currency), RequisitionReason.NewPosition, null, Today.AddMonths(2), "Build things.");

    private static Requisition Draft() => Requisition.Create("REQ-00001", Details(), Pakistan, Requester, Someone, Now);

    private static Requisition Submitted(params Guid[] approvers)
    {
        var requisition = Draft();
        requisition.Submit(approvers.Select((a, i) => new ApprovalStep($"Step {i + 1}", a)).ToList(), Someone, Today, Now);
        return requisition;
    }

    [Fact]
    public void REQ_01_new_requisition_is_a_logged_draft_in_the_country_currency()
    {
        var requisition = Draft();

        Assert.Equal(RequisitionStatus.Draft, requisition.Status);
        Assert.Equal(Money.Pkr(200), requisition.BudgetMaximum);
        Assert.Equal((null, RequisitionStatus.Draft), (requisition.StatusHistory[0].FromStatus, requisition.StatusHistory[0].ToStatus));
    }

    [Theory]
    [InlineData(100, 200, "USD", "currencyCode")]
    [InlineData(300, 200, "PKR", "budgetMax")]
    [InlineData(0, 200, "PKR", "budgetMax")]
    public void MC_02_budget_is_a_positive_range_in_local_currency(decimal min, decimal max, string currency, string field)
    {
        var ex = Assert.Throws<DomainValidationException>(() => Requisition.Create("REQ-1", Details(min, max, currency), Pakistan, Requester, Someone, Now));

        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void REQ_02_single_CEO_approval_opens_the_position()
    {
        var requisition = Submitted(Ceo);

        var outcome = requisition.Decide(Ceo, approve: true, "OK", Someone, Now);

        Assert.Equal(new DecisionOutcome(true, true, null), outcome);
        Assert.Equal(RequisitionStatus.Open, requisition.Status);
        Assert.Equal([RequisitionStatus.Draft, RequisitionStatus.PendingApproval, RequisitionStatus.Open], requisition.StatusHistory.Select(h => h.ToStatus));
    }

    [Fact]
    public void REQ_02_steps_run_in_order()
    {
        var requisition = Submitted(Ceo, Cfo);

        Assert.Equal(Ceo, requisition.CurrentStep!.ApproverEmployeeId);
        Assert.Throws<InvalidOperationException>(() => requisition.Decide(Cfo, true, null, Someone, Now));

        var outcome = requisition.Decide(Ceo, true, null, Someone, Now);
        Assert.Equal(Cfo, outcome.NextStep!.ApproverEmployeeId);
        Assert.Equal(RequisitionStatus.PendingApproval, requisition.Status);

        requisition.Decide(Cfo, true, null, Someone, Now);
        Assert.Equal(RequisitionStatus.Open, requisition.Status);
    }

    [Fact]
    public void REQ_02_rejection_needs_a_reason_and_returns_to_draft_closing_later_steps()
    {
        var requisition = Submitted(Ceo, Cfo);

        Assert.Equal("comment", Assert.Throws<DomainValidationException>(() => requisition.Decide(Ceo, false, " ", Someone, Now)).Field);
        requisition.Decide(Ceo, false, "Too expensive", Someone, Now);

        Assert.Equal(RequisitionStatus.Draft, requisition.Status);
        Assert.Equal([ApprovalDecision.Rejected, ApprovalDecision.Closed], requisition.Approvals.Select(a => a.Decision));
        Assert.Contains("Too expensive", requisition.StatusHistory[^1].Comment);
        Assert.Null(requisition.CurrentStep);
    }

    [Fact]
    public void REQ_02_resubmission_starts_a_new_round_and_keeps_the_old_one()
    {
        var requisition = Submitted(Ceo);
        requisition.Decide(Ceo, false, "Revise", Someone, Now);

        requisition.Submit([new ApprovalStep("CEO", Ceo)], Someone, Today, Now);

        Assert.Equal(2, requisition.ApprovalRound);
        Assert.Equal([(1, ApprovalDecision.Rejected), (2, ApprovalDecision.Pending)], requisition.Approvals.Select(a => (a.Round, a.Decision)));
    }

    [Fact]
    public void REQ_02_requester_cannot_approve_their_own_requisition()
    {
        var requisition = Submitted(Requester);

        Assert.Throws<DomainValidationException>(() => requisition.Decide(Requester, true, null, Someone, Now));
    }

    [Fact]
    public void REQ_02_submitting_needs_a_chain_and_a_future_target_date()
    {
        Assert.Equal("approvalChain", Assert.Throws<DomainValidationException>(() => Draft().Submit([], Someone, Today, Now)).Field);
        Assert.Equal("targetDate", Assert.Throws<DomainValidationException>(() =>
            Draft().Submit([new ApprovalStep("CEO", Ceo)], Someone, Today.AddYears(1), Now)).Field);
    }

    [Theory]
    [InlineData(RequisitionStatus.OnHold)]
    [InlineData(RequisitionStatus.Filled)]
    [InlineData(RequisitionStatus.Cancelled)]
    public void REQ_04_open_positions_can_be_held_filled_or_cancelled(RequisitionStatus status)
    {
        var requisition = Submitted(Ceo);
        requisition.Decide(Ceo, true, null, Someone, Now);

        requisition.ChangeStatus(status, Someone, Now, null);

        Assert.Equal(status, requisition.Status);
    }

    [Fact]
    public void REQ_04_filled_and_cancelled_are_final_and_drafts_cannot_be_held()
    {
        var filled = Submitted(Ceo);
        filled.Decide(Ceo, true, null, Someone, Now);
        filled.ChangeStatus(RequisitionStatus.Filled, Someone, Now, null);

        Assert.Empty(filled.ManualNextStatuses);
        Assert.Throws<DomainValidationException>(() => filled.ChangeStatus(RequisitionStatus.Open, Someone, Now, null));
        Assert.Throws<DomainValidationException>(() => Draft().ChangeStatus(RequisitionStatus.OnHold, Someone, Now, null));
        Assert.Throws<DomainValidationException>(() => Submitted(Ceo).Update(Details(), Pakistan));
    }

    [Fact]
    public void REQ_04_cancelling_a_pending_requisition_closes_its_approvals()
    {
        var requisition = Submitted(Ceo);

        requisition.ChangeStatus(RequisitionStatus.Cancelled, Someone, Now, "No longer needed");

        Assert.Equal(ApprovalDecision.Closed, requisition.Approvals.Single().Decision);
        Assert.Null(requisition.CurrentStep);
    }

    [Fact]
    public void REQ_01_replacement_must_name_someone_from_the_same_department()
    {
        var employee = Employee.Create(
            new Person("A", "B"), "PK-1", "a@b.co", EmploymentType.Permanent,
            new JobPlacement("Dev", new LegalEntity("PK", "PK Ltd"), Lahore, new Department("FIN", "Finance"), null), null, Today, new EmploymentTerms(3, 30));

        Assert.Throws<DomainValidationException>(() => Requisition.Create("R", Details() with { Reason = RequisitionReason.Replacement }, Pakistan, Requester, Someone, Now));
        Assert.Throws<DomainValidationException>(() => Requisition.Create(
            "R", Details() with { Reason = RequisitionReason.Replacement, ReplacedEmployee = employee }, Pakistan, Requester, Someone, Now));
    }

    [Fact]
    public void Approval_chain_needs_one_to_five_distinct_current_approvers()
    {
        var a = Employee.Create(new Person("A", "B"), "X1", "x1@b.co", EmploymentType.Permanent,
            new JobPlacement("CEO", new LegalEntity("PK", "PK Ltd"), Lahore, Engineering, null), null, Today, new EmploymentTerms(0, 30));

        Assert.Throws<DomainValidationException>(() => new ApprovalChain("PK", []));
        Assert.Throws<DomainValidationException>(() => new ApprovalChain("PK", [("CEO", a), ("Again", a)]));
        Assert.Equal("CEO", new ApprovalChain("PK", [("CEO", a)]).Steps.Single().Label);
    }
}

public class RequisitionRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly Guid Department = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();

    private static UserAccess User(string[] roles, Guid? employeeId = null, Guid? department = null, string country = "PK") =>
        new(roles.ToHashSet(), employeeId ?? Guid.NewGuid(), department ?? Guid.NewGuid(), country, null);

    private static Requisition Draft(Guid requester)
    {
        var details = new RequisitionDetails(
            "Engineer", new Department("ENG", "Engineering"), new OfficeLocation("PK", "Lahore", null, null), 1, Money.Pkr(1), Money.Pkr(2),
            RequisitionReason.NewPosition, null, new DateOnly(2030, 1, 1), "x");
        return Requisition.Create("R", details, new CountrySettings("PK", "Pakistan", "PKR", "Asia/Karachi"), requester, new Actor("u", "U", requester), Now);
    }

    [Theory]
    [InlineData(Roles.LineManager, true)]
    [InlineData(Roles.HiringManager, true)]
    [InlineData(Roles.Employee, false)]
    [InlineData(Roles.Interviewer, false)]
    public void REQ_01_managers_create_for_their_own_department_only(string role, bool expected)
    {
        var user = User([role], department: Department);

        Assert.Equal(expected, RequisitionRules.CanCreate(user, Department, "PK"));
        Assert.False(RequisitionRules.CanCreate(user, Guid.NewGuid(), "PK"));
    }

    [Fact]
    public void MC_03_HR_creates_in_their_countries()
    {
        var executive = User([Roles.HrExecutive], country: "PK");

        Assert.True(RequisitionRules.CanCreate(executive, Guid.NewGuid(), "PK"));
        Assert.False(RequisitionRules.CanCreate(executive, Guid.NewGuid(), "US"));
    }

    [Fact]
    public void Requester_manages_their_draft_and_HR_gets_every_status_option()
    {
        var draft = Draft(Me);

        var requester = RequisitionRules.For(User([Roles.LineManager], Me), draft);
        var colleague = RequisitionRules.For(User([Roles.LineManager]), draft);
        var hr = RequisitionRules.For(User([Roles.HrExecutive]), draft);

        Assert.True(requester is { CanEdit: true, CanSubmit: true });
        Assert.Equal([RequisitionStatus.Cancelled], requester.StatusOptions);
        Assert.True(colleague is { CanEdit: false, CanSubmit: false });
        Assert.Empty(colleague.StatusOptions);
        Assert.True(hr.CanEdit);
    }
}
