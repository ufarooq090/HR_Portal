using static HrPortal.Domain.Security.Capability;
using static HrPortal.Domain.Security.DataScope;

namespace HrPortal.Domain.Security;

/// <summary>
/// The "Access by module" table from docs/requirements.md ("Users and permissions"), the single source of
/// truth for role-based access. Endpoint policies are generated from it and data queries are filtered by the
/// scopes it grants. A module missing for a role means no access.
/// </summary>
/// <remarks>
/// System Admin has no module access (IT manages users, integrations and settings, with no access to salary
/// or performance content). HR Executive compensation access is "granted only": see <see cref="UserAccess"/>.
/// </remarks>
public static class PermissionMatrix
{
    private static readonly Dictionary<string, Dictionary<HrModule, ModuleAccess>> Matrix = new(StringComparer.Ordinal)
    {
        [Roles.SystemAdmin] = [],
        [Roles.HrAdmin] = new()
        {
            [HrModule.EmployeeRecords] = new(Full, Countries),
            [HrModule.Compensation] = new(Full, Countries),
            [HrModule.Requisitions] = new(Full | Approve, Countries),
            [HrModule.Candidates] = new(Full | Approve, Countries),
            [HrModule.ResumeBank] = new(Full, Countries),
            [HrModule.Onboarding] = new(Full, Countries),
            [HrModule.Offboarding] = new(Full, Countries),
            [HrModule.Attendance] = new(Full, Countries),
            [HrModule.Leave] = new(Full | Approve, Countries),
            [HrModule.Performance] = new(Full, Countries),
            [HrModule.EmailTemplates] = new(Full | Use, All),
            [HrModule.Dashboards] = new(View, Countries),
            [HrModule.AuditLog] = new(View, All),
        },
        [Roles.HrExecutive] = new()
        {
            [HrModule.EmployeeRecords] = new(Full, Countries),
            [HrModule.Requisitions] = new(Full, Countries),
            [HrModule.Candidates] = new(Full, Countries),
            [HrModule.ResumeBank] = new(Full, Countries),
            [HrModule.Onboarding] = new(Full, Countries),
            [HrModule.Offboarding] = new(Full, Countries),
            [HrModule.Attendance] = new(Full, Countries),
            [HrModule.Leave] = new(Full | Approve, Countries),
            [HrModule.Performance] = new(View, Countries),
            [HrModule.EmailTemplates] = new(Use, All),
            [HrModule.Dashboards] = new(View, Countries),
        },
        [Roles.HiringManager] = new()
        {
            [HrModule.EmployeeRecords] = new(View, Team),
            [HrModule.Requisitions] = new(View | Create, Department),
            [HrModule.Candidates] = new(View | Approve, Assigned),
            [HrModule.ResumeBank] = new(View, All),
            [HrModule.Onboarding] = new(View, Team),
            [HrModule.Offboarding] = new(View, Team),
            [HrModule.Attendance] = new(View, Team),
            [HrModule.Leave] = new(View | Approve, Team),
            [HrModule.Performance] = new(View | Edit, Team),

            // Interview templates only; the Templates module narrows this further.
            [HrModule.EmailTemplates] = new(Use, All),
            [HrModule.Dashboards] = new(View, Department),
        },
        [Roles.Interviewer] = new()
        {
            // Assigned candidates only; Create covers submitting scorecards.
            [HrModule.Candidates] = new(View | Create, Assigned),
        },
        [Roles.LineManager] = new()
        {
            [HrModule.EmployeeRecords] = new(View, Team),
            [HrModule.Requisitions] = new(View | Create, Department),
            [HrModule.Onboarding] = new(View | Edit, Assigned),
            [HrModule.Offboarding] = new(View | Edit, Assigned),
            [HrModule.Attendance] = new(View, Team),
            [HrModule.Leave] = new(View | Approve, Team),
            [HrModule.Performance] = new(View | Edit, Team),
            [HrModule.Dashboards] = new(View, Team),
        },
        [Roles.Employee] = new()
        {
            [HrModule.EmployeeRecords] = new(View, Own),
            [HrModule.Compensation] = new(View, Own),
            [HrModule.Onboarding] = new(View | Edit, Own),
            [HrModule.Offboarding] = new(View | Edit, Own),
            [HrModule.Attendance] = new(View, Own),
            [HrModule.Leave] = new(View | Create | Edit, Own),
            [HrModule.Performance] = new(View | Edit, Own),
        },
    };

    /// <summary>"Granted only": access an HR Admin can grant an HR Executive individually.</summary>
    public static readonly ModuleAccess GrantedCompensation = new(Full, Countries);

    /// <summary>Access a single role has to a module by default, or null for none.</summary>
    public static ModuleAccess? For(string role, HrModule module) =>
        Matrix.TryGetValue(role, out var modules) && modules.TryGetValue(module, out var access) ? access : null;

    /// <summary>
    /// Roles that may hold <paramref name="capability"/> on <paramref name="module"/> in some scope, including
    /// through an individual grant. Used for endpoint policies; the data scope is still checked per record.
    /// </summary>
    public static IReadOnlyList<string> RolesWith(HrModule module, Capability capability) =>
        Roles.All.Where(r => For(r, module)?.Allows(capability) == true
            || (r == Roles.HrExecutive && module == HrModule.Compensation && GrantedCompensation.Allows(capability)))
        .ToList();
}
