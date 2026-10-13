namespace HrPortal.Domain.Security;

/// <summary>
/// Everything needed to decide what one signed-in user may see: roles (from the identity provider), the
/// employee record they are linked to, and their HR country scope. Built once per request.
/// </summary>
public sealed class UserAccess
{
    public UserAccess(
        IReadOnlySet<string> roles,
        Guid? employeeId,
        Guid? departmentId,
        string? homeCountryCode,
        HrAccessSettings? hrSettings)
    {
        Roles = roles;
        EmployeeId = employeeId;
        DepartmentId = departmentId;
        HomeCountryCode = homeCountryCode;
        HrCountries = ResolveHrCountries(roles, homeCountryCode, hrSettings);
        CompensationGranted = hrSettings?.CompensationAccess == true;
    }

    public IReadOnlySet<string> Roles { get; }

    /// <summary>The signed-in user's employee record; null if their account is not linked to one.</summary>
    public Guid? EmployeeId { get; }

    public Guid? DepartmentId { get; }

    public string? HomeCountryCode { get; }

    /// <summary>Countries covered by <see cref="DataScope.Countries"/>. Empty for users without an HR role.</summary>
    public CountryScope HrCountries { get; }

    public bool CompensationGranted { get; }

    public bool IsHrUser => Roles.Contains(Security.Roles.HrAdmin) || Roles.Contains(Security.Roles.HrExecutive);

    /// <summary>The scopes in which the user holds <paramref name="capability"/> on <paramref name="module"/>, across all roles.</summary>
    public IReadOnlySet<DataScope> ScopesFor(HrModule module, Capability capability)
    {
        var scopes = new HashSet<DataScope>();
        foreach (var role in Roles)
        {
            if (PermissionMatrix.For(role, module) is { } access && access.Allows(capability))
            {
                scopes.Add(access.Scope);
            }
        }

        if (module == HrModule.Compensation && CompensationGranted && Roles.Contains(Security.Roles.HrExecutive)
            && PermissionMatrix.GrantedCompensation.Allows(capability))
        {
            scopes.Add(PermissionMatrix.GrantedCompensation.Scope);
        }

        // Scopes that can never match anything for this user are dropped, so "no scopes" means "no access".
        if (EmployeeId is null)
        {
            scopes.Remove(DataScope.Own);
            scopes.Remove(DataScope.Team);
        }

        if (DepartmentId is null)
        {
            scopes.Remove(DataScope.Department);
        }

        if (HrCountries.IsEmpty)
        {
            scopes.Remove(DataScope.Countries);
        }

        return scopes;
    }

    public bool HasAny(HrModule module, Capability capability) => ScopesFor(module, capability).Count > 0;

    /// <summary>
    /// Whether the user may change organisation setup (settings, legal entities, offices, grades) for a country.
    /// HR Admins are limited to the countries they can access; System Admins manage settings for every country.
    /// </summary>
    public bool CanConfigureCountry(string countryCode) =>
        Roles.Contains(Security.Roles.SystemAdmin)
        || (Roles.Contains(Security.Roles.HrAdmin) && HrCountries.Includes(countryCode));

    /// <summary>
    /// An HR Admin may change another HR user's access, but never their own, never for someone outside
    /// their countries, and never beyond the countries they can see themselves.
    /// </summary>
    public bool CanGrantHrAccess(Guid targetEmployeeId, string targetCountryCode, CountryScope requested) =>
        Roles.Contains(Security.Roles.HrAdmin)
        && targetEmployeeId != EmployeeId
        && HrCountries.Includes(targetCountryCode)
        && HrCountries.Covers(requested);

    private static CountryScope ResolveHrCountries(IReadOnlySet<string> roles, string? homeCountryCode, HrAccessSettings? settings)
    {
        if (!roles.Contains(Security.Roles.HrAdmin) && !roles.Contains(Security.Roles.HrExecutive))
        {
            return CountryScope.None;
        }

        if (settings is not null)
        {
            return settings.Countries;
        }

        if (roles.Contains(Security.Roles.HrAdmin))
        {
            return CountryScope.All;
        }

        return homeCountryCode is null ? CountryScope.None : CountryScope.Of([homeCountryCode]);
    }
}
