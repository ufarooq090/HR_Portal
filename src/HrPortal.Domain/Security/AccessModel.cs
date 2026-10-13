namespace HrPortal.Domain.Security;

/// <summary>Modules in the "Access by module" table of the requirements.</summary>
public enum HrModule
{
    EmployeeRecords,
    Compensation,
    Requisitions,
    Candidates,
    ResumeBank,
    Onboarding,
    Offboarding,
    Attendance,
    Leave,
    Performance,
    EmailTemplates,
    Dashboards,
    AuditLog,
}

/// <summary>What a role may do in a module. "Full" in the spec is <see cref="Full"/>.</summary>
[Flags]
public enum Capability
{
    None = 0,
    View = 1,
    Create = 2,
    Edit = 4,
    Delete = 8,
    Approve = 16,

    /// <summary>Use without editing, e.g. send from an email template.</summary>
    Use = 32,

    Full = View | Create | Edit | Delete,
}

/// <summary>Which records a capability applies to. A user's scopes from all their roles are combined (union).</summary>
public enum DataScope
{
    /// <summary>The user's own employee record and the data attached to it.</summary>
    Own,

    /// <summary>
    /// Items assigned to the user: interviews they sit on, onboarding/offboarding tasks they own,
    /// and for hiring managers the candidates on their own requisitions.
    /// </summary>
    Assigned,

    /// <summary>The user's direct and indirect reports.</summary>
    Team,

    /// <summary>The user's own department.</summary>
    Department,

    /// <summary>Every record in the countries the HR user may access (MC-03).</summary>
    Countries,

    /// <summary>Every record, not limited by country (e.g. the Resume Bank, which holds candidates not yet tied to a country).</summary>
    All,
}

public readonly record struct ModuleAccess(Capability Capabilities, DataScope Scope)
{
    public bool Allows(Capability capability) => capability != Capability.None && (Capabilities & capability) == capability;
}
