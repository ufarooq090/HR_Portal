using System.Net.Mail;
using HrPortal.Domain.Common;

namespace HrPortal.Domain.Employees;

/// <summary>
/// Anyone the company has dealt with. One record per human: a hired candidate's <see cref="Employee"/>
/// links to the same Person so hiring history is never duplicated.
/// </summary>
public sealed class Person : Entity
{
    public const int MaxEmergencyContacts = 3;

    private readonly List<EmergencyContact> _emergencyContacts = [];

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

    public string? PersonalEmail { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>Last ten digits of <see cref="Phone"/>, so 0300 1234567 and +92 300 1234567 match (CT-09).</summary>
    public string? PhoneKey { get; private set; }

    public DateOnly? DateOfBirth { get; private set; }

    public string? AddressLine1 { get; private set; }

    public string? AddressLine2 { get; private set; }

    public string? City { get; private set; }

    /// <summary>Province or state.</summary>
    public string? Region { get; private set; }

    public string? PostalCode { get; private set; }

    public string? AddressCountryCode { get; private set; }

    /// <summary>CNIC in Pakistan, SSN in the US. Encrypted at rest; shown only to HR and the person.</summary>
    [Sensitive]
    public string? NationalId { get; private set; }

    public IReadOnlyList<EmergencyContact> EmergencyContacts => _emergencyContacts;

    public void Rename(string givenName, string familyName)
    {
        GivenName = Guard.RequiredText(nameof(givenName), givenName, 100);
        FamilyName = Guard.RequiredText(nameof(familyName), familyName, 100);
    }

    public void UpdatePersonalDetails(PersonalDetails details, DateOnly today)
    {
        if (details.DateOfBirth is { } dob && (dob > today || dob < today.AddYears(-120)))
        {
            throw new DomainValidationException("dateOfBirth", "Enter a real date of birth in the past.");
        }

        if (details.EmergencyContacts.Count > MaxEmergencyContacts)
        {
            throw new DomainValidationException("emergencyContacts", $"Add at most {MaxEmergencyContacts} emergency contacts.");
        }

        PersonalEmail = OptionalEmail("personalEmail", details.PersonalEmail);
        SetPhone(details.Phone);
        DateOfBirth = details.DateOfBirth;
        AddressLine1 = Guard.OptionalText("addressLine1", details.AddressLine1, 200);
        AddressLine2 = Guard.OptionalText("addressLine2", details.AddressLine2, 200);
        City = Guard.OptionalText("city", details.City, 100);
        Region = Guard.OptionalText("region", details.Region, 100);
        PostalCode = Guard.OptionalText("postalCode", details.PostalCode, 20);
        AddressCountryCode = string.IsNullOrWhiteSpace(details.AddressCountryCode)
            ? null
            : Guard.CountryCode("addressCountryCode", details.AddressCountryCode);
        NationalId = Guard.OptionalText("nationalId", details.NationalId, 32);

        _emergencyContacts.Clear();
        _emergencyContacts.AddRange(details.EmergencyContacts.Select(c => new EmergencyContact(Id, c.Name, c.Relationship, c.Phone)));
    }

    /// <summary>Candidate contact details (CT-02); the rest of the personal details are left as they are.</summary>
    public void UpdateContact(string? email, string? phone)
    {
        PersonalEmail = OptionalEmail("email", email);
        SetPhone(phone);
    }

    /// <summary>Digits only, last ten; null when too short to identify anyone.</summary>
    public static string? PhoneKeyOf(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length < 7 ? null : digits.Length > 10 ? digits[^10..] : digits;
    }

    private void SetPhone(string? phone)
    {
        Phone = Guard.OptionalText("phone", phone, 32);
        PhoneKey = PhoneKeyOf(Phone);
    }

    public static string RequiredEmail(string field, string? value) =>
        OptionalEmail(field, value) ?? throw new DomainValidationException(field, "This field is required.");

    public static string? OptionalEmail(string field, string? value)
    {
        var email = Guard.OptionalText(field, value, 256)?.ToLowerInvariant();
        if (email is not null && (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || !email.Contains('.')))
        {
            throw new DomainValidationException(field, "Enter a valid email address.");
        }

        return email;
    }
}

public sealed record EmergencyContactDetails(string Name, string Relationship, string Phone);

/// <summary>Everything editable about a person's private life (EMP-01). Null clears a value.</summary>
public sealed record PersonalDetails(
    string? PersonalEmail,
    string? Phone,
    DateOnly? DateOfBirth,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? Region,
    string? PostalCode,
    string? AddressCountryCode,
    string? NationalId,
    IReadOnlyList<EmergencyContactDetails> EmergencyContacts)
{
    public static readonly PersonalDetails Empty = new(null, null, null, null, null, null, null, null, null, null, []);
}

public sealed class EmergencyContact : Entity
{
    private EmergencyContact()
    {
    }

    internal EmergencyContact(Guid personId, string name, string relationship, string phone)
    {
        PersonId = personId;
        Name = Guard.RequiredText("emergencyContacts", name, 200);
        Relationship = Guard.RequiredText("emergencyContacts", relationship, 50);
        Phone = Guard.RequiredText("emergencyContacts", phone, 32);
    }

    public Guid PersonId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Relationship { get; private set; } = null!;

    public string Phone { get; private set; } = null!;
}
