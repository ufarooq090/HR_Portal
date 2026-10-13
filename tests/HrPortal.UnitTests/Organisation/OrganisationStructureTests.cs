using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;

namespace HrPortal.UnitTests.Organisation;

public class OrganisationStructureTests
{
    private static readonly CountrySettings Pakistan = new("PK", "Pakistan", "PKR", "Asia/Karachi");

    [Fact]
    public void MC_01_office_inherits_the_country_time_zone_unless_it_sets_its_own()
    {
        var inherits = new OfficeLocation("PK", "Lahore Office", "Lahore", null);
        var overrides = new OfficeLocation("PK", "Remote Desk", null, "Asia/Dubai");

        Assert.Equal("Asia/Karachi", inherits.EffectiveTimeZone(Pakistan));
        Assert.Equal("Asia/Dubai", overrides.EffectiveTimeZone(Pakistan));
    }

    [Fact]
    public void MC_01_grade_overrides_cascade_over_country_employment_defaults()
    {
        var noOverride = new Grade("PK", "G1", "Associate", 1, null, null);
        var managerGrade = new Grade("PK", "G3", "Manager", 3, probationMonths: 6, noticePeriodDays: 60);

        Assert.Equal(new EmploymentTerms(3, 30), EmploymentTerms.For(Pakistan, null));
        Assert.Equal(new EmploymentTerms(3, 30), EmploymentTerms.For(Pakistan, noOverride));
        Assert.Equal(new EmploymentTerms(6, 60), EmploymentTerms.For(Pakistan, managerGrade));
    }

    [Fact]
    public void Grade_from_another_country_cannot_resolve_terms()
    {
        var usGrade = new Grade("US", "G1", "Associate", 1, null, null);

        Assert.Throws<ArgumentException>(() => EmploymentTerms.For(Pakistan, usGrade));
    }

    [Theory]
    [InlineData(25, null, "probationMonths")]
    [InlineData(null, 400, "noticePeriodDays")]
    public void Grade_overrides_use_the_same_limits_as_the_country(int? probation, int? notice, string field)
    {
        var ex = Assert.Throws<DomainValidationException>(() => new Grade("PK", "G1", "Associate", 1, probation, notice));

        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void Codes_are_normalised_and_names_trimmed()
    {
        var department = new Department(" eng ", "  Engineering ");
        var entity = new LegalEntity("pk", "Example (Pvt) Ltd");

        Assert.Equal("ENG", department.Code);
        Assert.Equal("Engineering", department.Name);
        Assert.Equal("PK", entity.CountryCode);
    }

    [Fact]
    public void Blank_names_are_rejected()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new LegalEntity("PK", "  "));

        Assert.Equal("name", ex.Field);
    }

    [Fact]
    public void MC_01_employee_takes_country_from_legal_entity_and_office()
    {
        var employee = NewEmployee(new LegalEntity("PK", "PK Ltd"), new OfficeLocation("PK", "Lahore", null, null));

        Assert.Equal("PK", employee.CountryCode);
    }

    [Fact]
    public void MC_01_employee_office_must_be_in_the_legal_entity_country()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            NewEmployee(new LegalEntity("PK", "PK Ltd"), new OfficeLocation("US", "US Office", null, null)));

        Assert.Equal("officeLocation", ex.Field);
    }

    [Fact]
    public void MC_01_employee_grade_must_be_in_the_employee_country()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            NewEmployee(new LegalEntity("PK", "PK Ltd"), new OfficeLocation("PK", "Lahore", null, null), new Grade("US", "G1", "Associate", 1, null, null)));

        Assert.Equal("grade", ex.Field);
    }

    [Fact]
    public void Employee_cannot_report_to_themselves()
    {
        var employee = NewEmployee(new LegalEntity("PK", "PK Ltd"), new OfficeLocation("PK", "Lahore", null, null));

        Assert.Throws<DomainValidationException>(() => employee.SetManager(employee));
    }

    private static Employee NewEmployee(LegalEntity entity, OfficeLocation office, Grade? grade = null) =>
        Employee.Create(
            new Person("Ayesha", "Khan"), "pk-0001", "Ayesha.Khan@Example.com", "Engineer",
            entity, office, new Department("ENG", "Engineering"), grade, new DateOnly(2026, 1, 1));
}
