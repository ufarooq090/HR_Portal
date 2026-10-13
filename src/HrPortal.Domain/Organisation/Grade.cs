using HrPortal.Domain.Common;

namespace HrPortal.Domain.Organisation;

/// <summary>A job grade within a country. May override the country's probation and notice defaults.</summary>
public sealed class Grade : Entity
{
    private Grade()
    {
    }

    public Grade(string countryCode, string code, string name, int level, int? probationMonths, int? noticePeriodDays)
    {
        CountryCode = Guard.CountryCode(nameof(countryCode), countryCode);
        Update(code, name, level, probationMonths, noticePeriodDays, isActive: true);
    }

    /// <summary>Fixed once created.</summary>
    public string CountryCode { get; private set; } = null!;

    /// <summary>Unique within the country, e.g. G3.</summary>
    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>Seniority order; higher is more senior.</summary>
    public int Level { get; private set; }

    /// <summary>Overrides the country default when set.</summary>
    public int? ProbationMonths { get; private set; }

    /// <summary>Overrides the country default when set.</summary>
    public int? NoticePeriodDays { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string code, string name, int level, int? probationMonths, int? noticePeriodDays, bool isActive)
    {
        Code = Guard.RequiredText(nameof(code), code, 20).ToUpperInvariant();
        Name = Guard.RequiredText(nameof(name), name, 100);
        Level = Guard.Range(nameof(level), level, 0, 100, "Level must be between 0 and 100.");
        ProbationMonths = probationMonths is { } p ? EmploymentTerms.ValidProbation(nameof(probationMonths), p) : null;
        NoticePeriodDays = noticePeriodDays is { } n ? EmploymentTerms.ValidNotice(nameof(noticePeriodDays), n) : null;
        IsActive = isActive;
    }
}
