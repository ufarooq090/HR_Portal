using HrPortal.Domain.Common;
using HrPortal.Domain.Security;
using static HrPortal.Domain.Security.DataScope;

namespace HrPortal.UnitTests.Security;

public class UserAccessTests
{
    private static readonly Guid Self = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Dept = Guid.NewGuid();

    private static UserAccess User(string[] roles, string? country = "PK", HrAccessSettings? settings = null, bool linked = true) =>
        new(roles.ToHashSet(), linked ? Self : null, linked ? Dept : null, linked ? country : null, settings);

    [Fact]
    public void NFR_02_scopes_from_several_roles_are_combined()
    {
        var manager = User([Roles.LineManager, Roles.Employee]);

        Assert.Equal([Own, Team], manager.ScopesFor(HrModule.EmployeeRecords, Capability.View).Order());
    }

    [Fact]
    public void NFR_02_a_capability_the_roles_lack_yields_no_scope()
    {
        var manager = User([Roles.LineManager, Roles.Employee]);

        Assert.Empty(manager.ScopesFor(HrModule.EmployeeRecords, Capability.Edit));
        Assert.False(manager.HasAny(HrModule.AuditLog, Capability.View));
    }

    [Fact]
    public void Own_and_team_scopes_need_a_linked_employee_record()
    {
        var unlinked = User([Roles.LineManager, Roles.Employee], linked: false);

        Assert.Empty(unlinked.ScopesFor(HrModule.EmployeeRecords, Capability.View));
    }

    [Fact]
    public void MC_03_HR_admin_sees_every_country_by_default()
    {
        Assert.True(User([Roles.HrAdmin]).HrCountries.IsAll);
    }

    [Fact]
    public void MC_03_HR_executive_sees_only_their_home_country_by_default()
    {
        Assert.Equal(CountryScope.Of(["PK"]), User([Roles.HrExecutive], "PK").HrCountries);
    }

    [Fact]
    public void MC_03_HR_executive_without_a_linked_record_sees_no_country()
    {
        var unlinked = User([Roles.HrExecutive], linked: false);

        Assert.True(unlinked.HrCountries.IsEmpty);
        Assert.Empty(unlinked.ScopesFor(HrModule.EmployeeRecords, Capability.View));
    }

    [Fact]
    public void MC_03_settings_replace_the_default_country_scope()
    {
        var limitedAdmin = User([Roles.HrAdmin], settings: new HrAccessSettings(Self, CountryScope.Of(["PK"]), false));
        var widenedExec = User([Roles.HrExecutive], settings: new HrAccessSettings(Self, CountryScope.Of(["PK", "US"]), false));

        Assert.Equal(CountryScope.Of(["PK"]), limitedAdmin.HrCountries);
        Assert.Equal(CountryScope.Of(["PK", "US"]), widenedExec.HrCountries);
    }

    [Fact]
    public void Non_HR_roles_have_no_country_scope_even_with_settings()
    {
        var manager = User([Roles.LineManager], settings: new HrAccessSettings(Self, CountryScope.All, true));

        Assert.True(manager.HrCountries.IsEmpty);
        Assert.Empty(manager.ScopesFor(HrModule.Compensation, Capability.View));
    }

    [Fact]
    public void EMP_03_HR_executive_needs_a_grant_for_compensation()
    {
        var plain = User([Roles.HrExecutive, Roles.Employee]);
        var granted = User([Roles.HrExecutive, Roles.Employee], settings: new HrAccessSettings(Self, CountryScope.Of(["PK"]), true));

        Assert.Equal([Own], plain.ScopesFor(HrModule.Compensation, Capability.View));
        Assert.Empty(plain.ScopesFor(HrModule.Compensation, Capability.Edit));
        Assert.Equal([Own, Countries], granted.ScopesFor(HrModule.Compensation, Capability.View).Order());
        Assert.Equal([Countries], granted.ScopesFor(HrModule.Compensation, Capability.Edit));
    }

    [Fact]
    public void MC_03_HR_admin_can_grant_within_their_own_countries()
    {
        var admin = User([Roles.HrAdmin], settings: new HrAccessSettings(Self, CountryScope.Of(["PK"]), true));

        Assert.True(admin.CanGrantHrAccess(Other, "PK", CountryScope.Of(["PK"])));
        Assert.False(admin.CanGrantHrAccess(Other, "PK", CountryScope.Of(["PK", "US"])));
        Assert.False(admin.CanGrantHrAccess(Other, "PK", CountryScope.All));
        Assert.False(admin.CanGrantHrAccess(Other, "US", CountryScope.Of(["PK"])));
    }

    [Fact]
    public void MC_03_nobody_can_change_their_own_HR_access()
    {
        Assert.False(User([Roles.HrAdmin]).CanGrantHrAccess(Self, "PK", CountryScope.Of(["PK"])));
    }

    [Theory]
    [InlineData(Roles.HrExecutive)]
    [InlineData(Roles.SystemAdmin)]
    [InlineData(Roles.LineManager)]
    public void MC_03_only_HR_admins_can_grant_HR_access(string role)
    {
        Assert.False(User([role]).CanGrantHrAccess(Other, "PK", CountryScope.Of(["PK"])));
    }

    [Fact]
    public void MC_03_country_limited_HR_admin_configures_only_their_countries()
    {
        var admin = User([Roles.HrAdmin], settings: new HrAccessSettings(Self, CountryScope.Of(["PK"]), true));

        Assert.True(admin.CanConfigureCountry("PK"));
        Assert.False(admin.CanConfigureCountry("US"));
        Assert.True(User([Roles.SystemAdmin]).CanConfigureCountry("US"));
        Assert.False(User([Roles.HrExecutive]).CanConfigureCountry("PK"));
    }

    [Fact]
    public void EMP_01_managers_see_their_teams_personal_details_but_not_confidential_data()
    {
        var manager = User([Roles.LineManager, Roles.HiringManager, Roles.Employee]);

        Assert.Equal([Own, Team], manager.PersonalDataScopesFor(Capability.View).Order());
        Assert.Equal([Own], manager.ConfidentialDataScopesFor(Capability.View));
        Assert.Empty(manager.PersonalDataScopesFor(Capability.Edit));
    }

    [Fact]
    public void EMP_01_HR_get_personal_data_in_their_countries_and_only_HR_can_edit_it()
    {
        Assert.Equal([Own, Countries], User([Roles.HrExecutive, Roles.Employee]).ConfidentialDataScopesFor(Capability.View).Order());
        Assert.Equal([Own, Countries], User([Roles.HrExecutive, Roles.Employee]).PersonalDataScopesFor(Capability.View).Order());
        Assert.Equal([Countries], User([Roles.HrExecutive, Roles.Employee]).PersonalDataScopesFor(Capability.Edit));
        Assert.Empty(User([Roles.Employee]).PersonalDataScopesFor(Capability.Edit));
    }

    [Theory]
    [InlineData(Roles.HrAdmin, true)]
    [InlineData(Roles.HrExecutive, false)]
    [InlineData(Roles.SystemAdmin, false)]
    public void EMP_04_only_HR_admins_can_terminate(string role, bool expected)
    {
        Assert.Equal(expected, User([role]).CanTerminate);
    }

    [Fact]
    public void HR_access_settings_need_at_least_one_valid_country()
    {
        Assert.Throws<DomainValidationException>(() => new HrAccessSettings(Self, CountryScope.Of([]), false));
        Assert.Throws<DomainValidationException>(() => new HrAccessSettings(Self, CountryScope.Of(["Pakistan"]), false));
    }

    [Fact]
    public void Country_scope_comparisons()
    {
        var pk = CountryScope.Of(["pk"]);
        var both = CountryScope.Of(["US", "PK", "PK"]);

        Assert.Equal(["PK", "US"], both.Codes);
        Assert.True(both.Covers(pk));
        Assert.False(pk.Covers(both));
        Assert.False(pk.Covers(CountryScope.All));
        Assert.True(CountryScope.All.Covers(both));
        Assert.True(pk.Includes("pk"));
        Assert.False(CountryScope.None.Includes("PK"));
    }
}
