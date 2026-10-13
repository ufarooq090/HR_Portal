using HrPortal.Domain.Common;

namespace HrPortal.Domain.Organisation;

/// <summary>The company an employee is contracted with. Drives letterhead and payroll export (MC-09).</summary>
public sealed class LegalEntity : Entity
{
    private LegalEntity()
    {
    }

    public LegalEntity(string countryCode, string name)
    {
        CountryCode = Guard.CountryCode(nameof(countryCode), countryCode);
        Update(name, isActive: true);
    }

    /// <summary>Fixed once created; the entity's employees belong to this country.</summary>
    public string CountryCode { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public void Update(string name, bool isActive)
    {
        Name = Guard.RequiredText(nameof(name), name, 200);
        IsActive = isActive;
    }
}
