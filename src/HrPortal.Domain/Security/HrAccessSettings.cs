using HrPortal.Domain.Common;

namespace HrPortal.Domain.Security;

/// <summary>
/// Overrides an HR user's default data access (MC-03). Without a row, an HR Admin sees every country and an
/// HR Executive only their home country, with no compensation access. Keyed by employee, because grants follow
/// the person rather than a sign-in account.
/// </summary>
public sealed class HrAccessSettings
{
    private HrAccessSettings()
    {
    }

    public HrAccessSettings(Guid employeeId, CountryScope countries, bool compensationAccess)
    {
        EmployeeId = employeeId;
        Update(countries, compensationAccess);
    }

    public Guid EmployeeId { get; private set; }

    /// <summary>Country codes the user may access; null means every country.</summary>
    public string[]? CountryCodes { get; private set; }

    /// <summary>Lets an HR Executive view and edit compensation ("granted only" in the access matrix).</summary>
    public bool CompensationAccess { get; private set; }

    public CountryScope Countries => CountryCodes is null ? CountryScope.All : CountryScope.Of(CountryCodes);

    public void Update(CountryScope countries, bool compensationAccess)
    {
        foreach (var code in countries.Codes)
        {
            Guard.CountryCode("countryCodes", code);
        }

        if (countries.IsEmpty)
        {
            throw new DomainValidationException("countryCodes", "Choose at least one country, or all countries.");
        }

        CountryCodes = countries.IsAll ? null : [.. countries.Codes];
        CompensationAccess = compensationAccess;
    }
}
