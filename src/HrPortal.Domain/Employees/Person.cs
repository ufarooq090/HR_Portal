using HrPortal.Domain.Common;

namespace HrPortal.Domain.Employees;

/// <summary>
/// Anyone the company has dealt with. One record per human: a hired candidate's <see cref="Employee"/>
/// links to the same Person so hiring history is never duplicated.
/// </summary>
public sealed class Person : Entity
{
    private Person()
    {
    }

    public Person(string givenName, string familyName)
    {
        Rename(givenName, familyName);
    }

    public string GivenName { get; private set; } = null!;

    public string FamilyName { get; private set; } = null!;

    public string FullName => $"{GivenName} {FamilyName}";

    public void Rename(string givenName, string familyName)
    {
        GivenName = Guard.RequiredText(nameof(givenName), givenName, 100);
        FamilyName = Guard.RequiredText(nameof(familyName), familyName, 100);
    }
}
