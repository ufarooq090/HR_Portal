using HrPortal.Domain.Security;
using static HrPortal.Domain.Security.Capability;
using static HrPortal.Domain.Security.DataScope;

namespace HrPortal.UnitTests.Security;

/// <summary>
/// The "Access by module" table of docs/requirements.md, cell by cell. Columns: HR Admin, HR Executive,
/// Hiring Manager, Interviewer, Line Manager, Employee. Null = "None".
/// </summary>
public class PermissionMatrixTests
{
    private static readonly string[] Columns =
        [Roles.HrAdmin, Roles.HrExecutive, Roles.HiringManager, Roles.Interviewer, Roles.LineManager, Roles.Employee];

    public static TheoryData<HrModule, ModuleAccess?[]> SpecTable => new()
    {
        // Employee Records: Full | Full (no salary) | View team | None | View team | Own
        { HrModule.EmployeeRecords, [new(Full, Countries), new(Full, Countries), new(View, Team), null, new(View, Team), new(View, Own)] },

        // Salary and compensation: Full | Granted only | None | None | None | Own
        { HrModule.Compensation, [new(Full, Countries), null, null, null, null, new(View, Own)] },

        // Job Requisitions: Full | Full | Create, own dept | None | Create, own dept | None
        { HrModule.Requisitions, [new(Full | Approve, Countries), new(Full, Countries), new(View | Create, Department), null, new(View | Create, Department), null] },

        // Candidate Tracker: Full | Full | Own requisitions | Assigned | None | None
        { HrModule.Candidates, [new(Full | Approve, Countries), new(Full, Countries), new(View | Approve, Assigned), new(View | Create, Assigned), null, null] },

        // Resume Bank: Full | Full | Search, view | None | None | None
        // HR within their countries (decision 13 Oct 2026); hiring managers within their own country (MC-03).
        { HrModule.ResumeBank, [new(Full, Countries), new(Full, Countries), new(View, HomeCountry), null, null, null] },

        // Onboarding: Full | Full | View team | None | Assigned tasks | Own checklist
        { HrModule.Onboarding, [new(Full, Countries), new(Full, Countries), new(View, Team), null, new(View | Edit, Assigned), new(View | Edit, Own)] },

        // Offboarding: same as onboarding
        { HrModule.Offboarding, [new(Full, Countries), new(Full, Countries), new(View, Team), null, new(View | Edit, Assigned), new(View | Edit, Own)] },

        // Attendance: Full | Full | Team | None | Team | Own
        { HrModule.Attendance, [new(Full, Countries), new(Full, Countries), new(View, Team), null, new(View, Team), new(View, Own)] },

        // Leave: Full | Full | Approve team | None | Approve team | Own
        { HrModule.Leave, [new(Full | Approve, Countries), new(Full | Approve, Countries), new(View | Approve, Team), null, new(View | Approve, Team), new(View | Create | Edit, Own)] },

        // Performance: Full | View | Team | None | Team | Own
        { HrModule.Performance, [new(Full, Countries), new(View, Countries), new(View | Edit, Team), null, new(View | Edit, Team), new(View | Edit, Own)] },

        // Email Templates: Full | Use | Use (interview only) | None | None | None
        { HrModule.EmailTemplates, [new(Full | Use, All), new(Use, All), new(Use, All), null, null, null] },

        // Dashboards: All | All | Own dept | None | Team | None
        { HrModule.Dashboards, [new(View, Countries), new(View, Countries), new(View, Department), null, new(View, Team), null] },

        // Audit Log: View | None | None | None | None | None
        { HrModule.AuditLog, [new(View, All), null, null, null, null, null] },
    };

    [Theory]
    [MemberData(nameof(SpecTable))]
    public void NFR_02_matrix_matches_the_access_by_module_table(HrModule module, ModuleAccess?[] expected)
    {
        for (var i = 0; i < Columns.Length; i++)
        {
            Assert.True(
                expected[i] == PermissionMatrix.For(Columns[i], module),
                $"{Columns[i]} / {module}: expected {expected[i]?.ToString() ?? "none"}, got {PermissionMatrix.For(Columns[i], module)?.ToString() ?? "none"}");
        }
    }

    [Fact]
    public void Every_module_is_covered_by_the_spec_table()
    {
        Assert.Equal(Enum.GetValues<HrModule>().Order(), SpecTable.Select(row => (HrModule)row[0]).Order());
    }

    [Fact]
    public void System_admin_has_no_access_to_HR_data_including_salary_and_performance()
    {
        Assert.All(Enum.GetValues<HrModule>(), m => Assert.Null(PermissionMatrix.For(Roles.SystemAdmin, m)));
    }

    [Fact]
    public void Compensation_policy_admits_HR_executives_because_access_can_be_granted()
    {
        Assert.Equal(
            [Roles.HrAdmin, Roles.HrExecutive, Roles.Employee],
            PermissionMatrix.RolesWith(HrModule.Compensation, View));
        Assert.Equal([Roles.HrAdmin, Roles.HrExecutive], PermissionMatrix.RolesWith(HrModule.Compensation, Edit));
    }

    [Fact]
    public void Only_HR_admins_can_read_the_audit_log()
    {
        Assert.Equal([Roles.HrAdmin], PermissionMatrix.RolesWith(HrModule.AuditLog, View));
    }

    [Fact]
    public void Interviewers_reach_no_employee_records()
    {
        Assert.DoesNotContain(Roles.Interviewer, PermissionMatrix.RolesWith(HrModule.EmployeeRecords, View));
    }
}
