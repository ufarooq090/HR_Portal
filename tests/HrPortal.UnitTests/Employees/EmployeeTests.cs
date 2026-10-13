using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;

namespace HrPortal.UnitTests.Employees;

public class EmployeeTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly CountrySettings Pakistan = new("PK", "Pakistan", "PKR", "Asia/Karachi");
    private static readonly LegalEntity PkLtd = new("PK", "PK Ltd");
    private static readonly OfficeLocation Lahore = new("PK", "Lahore", null, null);
    private static readonly Department Engineering = new("ENG", "Engineering");
    private static readonly Department Finance = new("FIN", "Finance");

    private static Employee NewEmployee(
        LegalEntity? entity = null, OfficeLocation? office = null, Grade? grade = null, Employee? manager = null, DateOnly? joining = null) =>
        Employee.Create(
            new Person("Ayesha", "Khan"), "pk-0001", "Ayesha.Khan@Example.com", EmploymentType.Permanent,
            new JobPlacement("Engineer", entity ?? PkLtd, office ?? Lahore, Engineering, grade),
            manager, joining ?? new DateOnly(2026, 1, 1), EmploymentTerms.For(Pakistan, grade));

    [Fact]
    public void MC_01_employee_takes_country_from_legal_entity_and_office()
    {
        var employee = NewEmployee();

        Assert.Equal("PK", employee.CountryCode);
        Assert.Equal("PK-0001", employee.EmployeeNumber);
        Assert.Equal("ayesha.khan@example.com", employee.WorkEmail);
    }

    [Fact]
    public void MC_01_employee_office_must_be_in_the_legal_entity_country()
    {
        var ex = Assert.Throws<DomainValidationException>(() => NewEmployee(office: new OfficeLocation("US", "US Office", null, null)));

        Assert.Equal("officeLocationId", ex.Field);
    }

    [Fact]
    public void MC_01_employee_grade_must_be_in_the_employee_country()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Employee.Create(
                new Person("A", "B"), "X1", "a@b.co", EmploymentType.Permanent,
                new JobPlacement("Engineer", PkLtd, Lahore, Engineering, new Grade("US", "G1", "Associate", 1, null, null)),
                null, Today, new EmploymentTerms(3, 30)));

        Assert.Equal("gradeId", ex.Field);
    }

    [Fact]
    public void EMP_04_new_employee_starts_on_probation_from_country_and_grade_terms()
    {
        var employee = NewEmployee(grade: new Grade("PK", "G3", "Manager", 3, 6, null));

        Assert.Equal(EmploymentStatus.Probation, employee.Status);
        Assert.Equal(new DateOnly(2026, 7, 1), employee.ProbationEndDate);
    }

    [Fact]
    public void EMP_04_no_probation_means_confirmed_from_day_one()
    {
        var employee = NewEmployee(grade: new Grade("PK", "G9", "Director", 9, 0, null));

        Assert.Equal(EmploymentStatus.Confirmed, employee.Status);
        Assert.Null(employee.ProbationEndDate);
    }

    [Fact]
    public void EMP_04_notice_sets_last_working_day_from_the_notice_period()
    {
        var employee = NewEmployee();

        employee.ChangeStatus(EmploymentStatus.OnNotice, new DateOnly(2026, 10, 1), null, new EmploymentTerms(3, 30), "Resigned", Today);

        Assert.Equal(EmploymentStatus.OnNotice, employee.Status);
        Assert.Equal(new DateOnly(2026, 10, 31), employee.LastWorkingDate);
    }

    [Fact]
    public void EMP_04_withdrawn_notice_returns_to_confirmed_and_clears_the_last_day()
    {
        var employee = NewEmployee();
        employee.ChangeStatus(EmploymentStatus.OnNotice, new DateOnly(2026, 10, 1), null, new EmploymentTerms(3, 30), null, Today);

        employee.ChangeStatus(EmploymentStatus.Confirmed, Today, null, new EmploymentTerms(3, 30), "Withdrawn", Today);

        Assert.Null(employee.LastWorkingDate);
    }

    [Theory]
    [InlineData(EmploymentStatus.Resigned, EmploymentStatus.Confirmed)]
    [InlineData(EmploymentStatus.Terminated, EmploymentStatus.OnNotice)]
    [InlineData(EmploymentStatus.Absconded, EmploymentStatus.Probation)]
    public void EMP_04_leaving_is_final(EmploymentStatus exit, EmploymentStatus next)
    {
        var employee = NewEmployee();
        employee.ChangeStatus(exit, Today, null, new EmploymentTerms(3, 30), null, Today);

        var ex = Assert.Throws<DomainValidationException>(() => employee.ChangeStatus(next, Today, null, new EmploymentTerms(3, 30), null, Today));

        Assert.Equal("status", ex.Field);
        Assert.Equal(Today, employee.LastWorkingDate);
    }

    [Fact]
    public void EMP_04_cannot_go_back_to_probation()
    {
        Assert.False(EmploymentStatusRules.CanChange(EmploymentStatus.Confirmed, EmploymentStatus.Probation));
        Assert.Empty(EmploymentStatusRules.NextStatuses(EmploymentStatus.Terminated));
    }

    [Fact]
    public void EMP_05_joining_records_a_snapshot()
    {
        var employee = NewEmployee();

        var joined = Assert.Single(employee.History);
        Assert.Equal(JobChangeKind.Joined, joined.Kind);
        Assert.Equal("Engineer", joined.Designation);
        Assert.Equal(employee.JoiningDate, joined.EffectiveDate);
    }

    [Fact]
    public void EMP_05_promotion_and_transfer_are_kept_as_dated_history()
    {
        var employee = NewEmployee();

        employee.ChangeJob(JobChangeKind.Promotion, new JobPlacement("Senior Engineer", PkLtd, Lahore, Engineering, null), new DateOnly(2026, 6, 1), "Annual review", Today);
        employee.ChangeJob(JobChangeKind.Transfer, new JobPlacement("Senior Engineer", PkLtd, Lahore, Finance, null), new DateOnly(2026, 9, 1), null, Today);

        Assert.Equal(["Joined", "Promotion", "Transfer"], employee.History.Select(h => h.Kind.ToString()));
        Assert.Equal("Senior Engineer", employee.History[1].Designation);
        Assert.Equal(Engineering.Id, employee.History[1].DepartmentId);
        Assert.Equal(Finance.Id, employee.History[2].DepartmentId);
        Assert.Equal(Finance.Id, employee.DepartmentId);
    }

    [Fact]
    public void EMP_05_a_change_that_changes_nothing_is_rejected()
    {
        var employee = NewEmployee();

        Assert.Throws<DomainValidationException>(() =>
            employee.ChangeJob(JobChangeKind.Correction, new JobPlacement("Engineer", PkLtd, Lahore, Engineering, null), Today, null, Today));
    }

    [Theory]
    [InlineData(2025, 12, 31)]
    [InlineData(2026, 10, 14)]
    public void EMP_05_changes_must_fall_between_joining_and_today(int y, int m, int d)
    {
        var employee = NewEmployee();

        var ex = Assert.Throws<DomainValidationException>(() =>
            employee.ChangeJob(JobChangeKind.Promotion, new JobPlacement("Lead", PkLtd, Lahore, Engineering, null), new DateOnly(y, m, d), null, Today));

        Assert.Equal("effectiveDate", ex.Field);
    }

    [Fact]
    public void EMP_05_manager_change_is_recorded_and_loops_are_refused()
    {
        var manager = NewEmployee();
        var employee = NewEmployee();

        employee.ChangeManager(manager, managerReportsToThisEmployee: false, Today, "Reorg", Today);

        Assert.Equal(manager.Id, employee.ManagerId);
        Assert.Equal(JobChangeKind.ManagerChange, employee.History[^1].Kind);
        Assert.Equal(manager.Id, employee.History[^1].ManagerId);
        Assert.Equal("managerId", Assert.Throws<DomainValidationException>(() => manager.ChangeManager(employee, true, Today, null, Today)).Field);
        Assert.Equal("managerId", Assert.Throws<DomainValidationException>(() => manager.ChangeManager(manager, false, Today, null, Today)).Field);
    }

    [Fact]
    public void EMP_05_people_who_have_left_cannot_change_job_or_manage_others()
    {
        var leaver = NewEmployee();
        leaver.ChangeStatus(EmploymentStatus.Resigned, Today, null, new EmploymentTerms(3, 30), null, Today);

        Assert.Throws<DomainValidationException>(() =>
            leaver.ChangeJob(JobChangeKind.Promotion, new JobPlacement("Lead", PkLtd, Lahore, Engineering, null), Today, null, Today));
        Assert.Equal("managerId", Assert.Throws<DomainValidationException>(() => NewEmployee(manager: leaver)).Field);
    }

    [Fact]
    public void EMP_03_compensation_is_stored_in_the_country_currency()
    {
        var employee = NewEmployee();

        var pay = Compensation.Create(employee, Pakistan, Money.Pkr(2_400_000.50m), "B2", employee.JoiningDate, CompensationReason.Joining, null);

        Assert.Equal(Money.Pkr(2_400_000.50m), pay.AnnualCtc);
        Assert.Equal("2400000.50", pay.AnnualCtcAmount);
    }

    [Fact]
    public void MC_02_compensation_in_another_currency_is_rejected()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Compensation.Create(NewEmployee(), Pakistan, Money.Usd(90_000), null, Today, CompensationReason.Increment, null));

        Assert.Equal("currencyCode", ex.Field);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.123)]
    public void EMP_03_amount_must_be_positive_with_two_decimals(decimal amount)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Compensation.Create(NewEmployee(), Pakistan, Money.Pkr(amount), null, Today, CompensationReason.Increment, null));

        Assert.Equal("annualCtc", ex.Field);
    }

    [Fact]
    public void EMP_03_compensation_cannot_start_before_joining_but_may_be_future_dated()
    {
        var employee = NewEmployee();

        Assert.Throws<DomainValidationException>(() =>
            Compensation.Create(employee, Pakistan, Money.Pkr(1), null, new DateOnly(2025, 12, 1), CompensationReason.Joining, null));
        Compensation.Create(employee, Pakistan, Money.Pkr(1), null, Today.AddMonths(2), CompensationReason.Increment, null);
    }

    [Fact]
    public void EMP_01_personal_details_are_validated_and_emergency_contacts_replaced()
    {
        var person = new Person("Ayesha", "Khan");

        person.UpdatePersonalDetails(
            PersonalDetails.Empty with
            {
                PersonalEmail = " Ayesha@Mail.com ",
                DateOfBirth = new DateOnly(1995, 5, 5),
                AddressCountryCode = "pk",
                NationalId = "35202-1234567-1",
                EmergencyContacts = [new("Bilal Khan", "Brother", "+92 300 1234567")],
            },
            Today);
        Assert.Equal("ayesha@mail.com", person.PersonalEmail);
        Assert.Equal("PK", person.AddressCountryCode);

        person.UpdatePersonalDetails(PersonalDetails.Empty with { EmergencyContacts = [new("Sara Khan", "Mother", "+92 300 7654321")] }, Today);

        Assert.Null(person.PersonalEmail);
        Assert.Equal("Sara Khan", Assert.Single(person.EmergencyContacts).Name);
    }

    [Theory]
    [InlineData("not-an-email", null, "personalEmail")]
    [InlineData(null, "2027-01-01", "dateOfBirth")]
    public void EMP_01_invalid_personal_details_are_rejected(string? email, string? dob, string field)
    {
        var ex = Assert.Throws<DomainValidationException>(() => new Person("A", "B").UpdatePersonalDetails(
            PersonalDetails.Empty with { PersonalEmail = email, DateOfBirth = dob is null ? null : DateOnly.Parse(dob) }, Today));

        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void EMP_01_at_most_three_emergency_contacts()
    {
        var contacts = Enumerable.Range(1, 4).Select(i => new EmergencyContactDetails($"C{i}", "Friend", "1")).ToList();

        Assert.Throws<DomainValidationException>(() =>
            new Person("A", "B").UpdatePersonalDetails(PersonalDetails.Empty with { EmergencyContacts = contacts }, Today));
    }
}
