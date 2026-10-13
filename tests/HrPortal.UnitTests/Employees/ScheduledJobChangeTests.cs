using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;

namespace HrPortal.UnitTests.Employees;

public class ScheduledJobChangeTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly LegalEntity PkLtd = new("PK", "PK Ltd");
    private static readonly OfficeLocation Lahore = new("PK", "Lahore", null, null);
    private static readonly Department Engineering = new("ENG", "Engineering");

    private static Employee NewEmployee() =>
        Employee.Create(
            new Person("Ayesha", "Khan"), Guid.NewGuid().ToString("N")[..8], $"{Guid.NewGuid():N}@x.test", EmploymentType.Permanent,
            new JobPlacement("Engineer", PkLtd, Lahore, Engineering, null), null, new DateOnly(2026, 1, 1), new EmploymentTerms(3, 30));

    private static JobPlacement Lead => new("Lead", PkLtd, Lahore, Engineering, null);

    [Fact]
    public void A_future_change_is_pending_until_applied()
    {
        var change = ScheduledJobChange.ForJob(NewEmployee(), JobChangeKind.Promotion, Lead, Today.AddDays(10), "Review", Today);

        Assert.Equal(ScheduledChangeStatus.Pending, change.Status);
        Assert.Equal("Lead", change.Designation);
        Assert.False(change.IsManagerChange);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Only_dates_after_today_can_be_scheduled(int days)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            ScheduledJobChange.ForJob(NewEmployee(), JobChangeKind.Promotion, Lead, Today.AddDays(days), null, Today));

        Assert.Equal("effectiveDate", ex.Field);
    }

    [Fact]
    public void Reporting_loop_is_refused_when_scheduling_a_manager_change()
    {
        var employee = NewEmployee();

        var ex = Assert.Throws<DomainValidationException>(() =>
            ScheduledJobChange.ForManager(employee, NewEmployee(), managerReportsToThisEmployee: true, Today.AddDays(5), null, Today));

        Assert.Contains("loop", ex.Message);
        Assert.Equal("managerId", Assert.Throws<DomainValidationException>(() =>
            ScheduledJobChange.ForManager(employee, employee, false, Today.AddDays(5), null, Today)).Field);
    }

    [Fact]
    public void Placement_must_stay_in_one_country()
    {
        Assert.Throws<DomainValidationException>(() => ScheduledJobChange.ForJob(
            NewEmployee(), JobChangeKind.Transfer, new JobPlacement("Lead", PkLtd, new OfficeLocation("US", "NY", null, null), Engineering, null), Today.AddDays(5), null, Today));
    }

    [Fact]
    public void Leavers_cannot_get_scheduled_changes()
    {
        var leaver = NewEmployee();
        leaver.ChangeStatus(EmploymentStatus.Resigned, Today, null, new EmploymentTerms(3, 30), null, Today);

        Assert.Throws<DomainValidationException>(() => ScheduledJobChange.ForJob(leaver, JobChangeKind.Promotion, Lead, Today.AddDays(5), null, Today));
    }

    [Fact]
    public void Only_pending_changes_can_be_edited_cancelled_or_applied()
    {
        var employee = NewEmployee();
        var change = ScheduledJobChange.ForJob(employee, JobChangeKind.Promotion, Lead, Today.AddDays(10), null, Today);

        change.UpdateJob(employee, Lead with { Designation = "Principal" }, Today.AddDays(20), "Changed", Today);
        change.Cancel("No longer needed", DateTimeOffset.UtcNow);

        Assert.Equal(("Principal", ScheduledChangeStatus.Cancelled), (change.Designation, change.Status));
        Assert.Throws<DomainValidationException>(() => change.UpdateJob(employee, Lead, Today.AddDays(30), null, Today));
        Assert.Throws<DomainValidationException>(() => change.MarkApplied(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Failed_change_keeps_the_reason()
    {
        var change = ScheduledJobChange.ForManager(NewEmployee(), null, false, Today.AddDays(1), null, Today);

        change.MarkFailed("The new manager reports to this employee; that would create a reporting loop.", DateTimeOffset.UtcNow);

        Assert.Equal(ScheduledChangeStatus.Failed, change.Status);
        Assert.Contains("loop", change.Outcome);
    }

    [Fact]
    public void Effective_dates_follow_the_office_time_zone()
    {
        var instant = new DateTimeOffset(2027, 5, 9, 20, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2027, 5, 10), LocalCalendar.Today(instant, "Asia/Karachi"));
        Assert.Equal(new DateOnly(2027, 5, 9), LocalCalendar.Today(instant, "America/New_York"));
    }
}
