using HrPortal.Domain.Common;

namespace HrPortal.Domain.Organisation;

/// <summary>A department. Departments span countries (e.g. Engineering in Pakistan and the US).</summary>
public sealed class Department : Entity
{
    private Department()
    {
    }

    public Department(string code, string name)
    {
        Update(code, name, isActive: true);
    }

    /// <summary>Short unique code, e.g. ENG.</summary>
    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public void Update(string code, string name, bool isActive)
    {
        Code = Guard.RequiredText(nameof(code), code, 20).ToUpperInvariant();
        Name = Guard.RequiredText(nameof(name), name, 200);
        IsActive = isActive;
    }
}
