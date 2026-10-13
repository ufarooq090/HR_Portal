using System.Globalization;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Employees;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Infrastructure.Recruiting;

/// <summary>
/// RB-12: imports HR's existing candidate sheets into the Resume Bank, following the employee import (EMP-07): every
/// row is validated first and nothing is saved unless the whole file is valid. People already in the portal (same
/// email or phone, candidates and employees alike) are reported, never duplicated (CT-09).
/// </summary>
public sealed class CandidateImporter(HrPortalDbContext db, TimeProvider clock)
{
    public const int MaxRows = 5000;

    public static readonly IReadOnlyList<ImportColumn> Columns =
    [
        new("GivenName", true, "First name."),
        new("FamilyName", true, "Last name."),
        new("Email", false, "Email or phone is required, to spot people already in the portal."),
        new("Phone", false, "Any format; 0300 1234567 and +92 300 1234567 are the same number."),
        new("Country", true, "Where the candidate is based: PK or US. HR in that country manage them."),
        new("Location", false, "City or area, e.g. Lahore."),
        new("CurrentEmployer", false, "Current or last employer."),
        new("ExperienceYears", false, "Total years of experience, e.g. 4.5."),
        new("NoticePeriodDays", false, "Days, e.g. 30."),
        new("Skills", false, "Separated by commas or semicolons, e.g. Python; SQL; Spark."),
        new("Tags", false, "Separated by commas or semicolons, e.g. good for future data roles."),
        new("CurrentCtc", false, "Annual, number only."),
        new("ExpectedCtc", false, "Annual, number only."),
        new("CtcCurrency", false, "PKR or USD. Empty: the country's currency."),
        new("Status", false, "Shortlisted, Not shortlisted, No-show, Offer declined, Resigned, Terminated or Hired. Blacklisting is done in the portal."),
        new("LastContacted", false, "Date as YYYY-MM-DD, or an Excel date cell."),
        new("Notes", false, "Becomes a note on the candidate."),
    ];

    public async Task<ImportResult> ImportAsync(Sheet sheet, UserAccess access, Actor actor, CancellationToken ct)
    {
        var (errors, created) = await ValidateAsync(sheet, access, actor, ct);
        if (errors.Count > 0)
        {
            return new ImportResult(sheet.Rows.Count, 0, errors);
        }

        foreach (var (person, profile, note) in created)
        {
            db.People.Add(person);
            db.CandidateProfiles.Add(profile);
            if (note is not null)
            {
                db.CandidateNotes.Add(note);
            }
        }

        await db.SaveChangesAsync(ct);
        return new ImportResult(sheet.Rows.Count, created.Count, []);
    }

    public async Task<byte[]> ErrorReportAsync(Sheet sheet, UserAccess access, Actor actor, CancellationToken ct)
    {
        var (errors, _) = await ValidateAsync(sheet, access, actor, ct);
        return ImportReport.ErrorReport("Candidates", sheet, errors);
    }

    public async Task<byte[]> TemplateAsync(CancellationToken ct) =>
        ImportReport.Template("Candidates", Columns,
        [
            ("Countries", await db.CountrySettings.OrderBy(c => c.CountryCode).Select(c => $"{c.CountryCode} – {c.Name} ({c.CurrencyCode})").ToListAsync(ct)),
        ]);

    private async Task<(List<ImportError> Errors, List<(Person, CandidateProfile, CandidateNote?)> Created)> ValidateAsync(
        Sheet sheet, UserAccess access, Actor actor, CancellationToken ct)
    {
        var errors = new List<ImportError>();
        var missing = Columns.Where(c => c.Required && !sheet.HasColumn(c.Name)).Select(c => c.Name).ToList();
        if (missing.Count > 0)
        {
            errors.Add(new(1, string.Join(", ", missing), "Required column missing. Download the template for the expected headers."));
            return (errors, []);
        }

        if (sheet.Rows.Count is 0 or > MaxRows)
        {
            errors.Add(new(1, string.Empty, sheet.Rows.Count == 0 ? "The file has no candidate rows." : $"Import at most {MaxRows} rows at a time."));
            return (errors, []);
        }

        var countries = await db.CountrySettings.ToDictionaryAsync(c => c.CountryCode, ct);
        var currencies = countries.Values.Select(c => c.CurrencyCode).ToHashSet(StringComparer.Ordinal);
        var emails = sheet.Rows.Select(r => r.Get("Email").Trim().ToLowerInvariant()).Where(e => e.Length > 0).ToList();
        var phoneKeys = sheet.Rows.Select(r => Person.PhoneKeyOf(r.Get("Phone"))).OfType<string>().ToList();
        var existing = await db.People
            .Where(p => (p.PersonalEmail != null && emails.Contains(p.PersonalEmail)) || (p.PhoneKey != null && phoneKeys.Contains(p.PhoneKey)))
            .Select(p => new { p.GivenName, p.FamilyName, p.PersonalEmail, p.PhoneKey })
            .ToListAsync(ct);
        var now = clock.GetUtcNow();
        var seenEmails = new Dictionary<string, int>();
        var seenPhones = new Dictionary<string, int>();
        var created = new List<(Person, CandidateProfile, CandidateNote?)>();

        foreach (var row in sheet.Rows)
        {
            var before = errors.Count;
            void Error(string column, string message) => errors.Add(new(row.RowNumber, column, message));

            var email = row.Get("Email").Trim().ToLowerInvariant();
            var phoneKey = Person.PhoneKeyOf(row.Get("Phone"));
            if (email.Length == 0 && phoneKey is null)
            {
                Error("Email", "Give an email or a phone number, so duplicates can be found.");
            }

            if (email.Length > 0 && existing.FirstOrDefault(p => p.PersonalEmail == email) is { } byEmail)
            {
                Error("Email", $"Already in the portal as {byEmail.GivenName} {byEmail.FamilyName}. Add them to a requisition from the Resume Bank instead.");
            }
            else if (email.Length > 0 && !seenEmails.TryAdd(email, row.RowNumber))
            {
                Error("Email", $"Also used on row {seenEmails[email]}.");
            }

            if (phoneKey is not null && existing.FirstOrDefault(p => p.PhoneKey == phoneKey) is { } byPhone)
            {
                Error("Phone", $"Already in the portal as {byPhone.GivenName} {byPhone.FamilyName}.");
            }
            else if (phoneKey is not null && !seenPhones.TryAdd(phoneKey, row.RowNumber))
            {
                Error("Phone", $"Also used on row {seenPhones[phoneKey]}.");
            }

            var countryCode = row.Get("Country").Trim().ToUpperInvariant();
            if (!countries.TryGetValue(countryCode, out var country))
            {
                Error("Country", $"Use one of: {string.Join(", ", countries.Keys.Order())}.");
            }
            else if (!ResumeBankRules.IsHrFor(access, countryCode, Capability.Create))
            {
                Error("Country", $"You cannot add candidates in {country.Name}.");
            }

            var status = ParseStatus(row, Error);
            var currency = row.Get("CtcCurrency").Trim().ToUpperInvariant();
            if (currency.Length == 0)
            {
                currency = country?.CurrencyCode ?? "PKR";
            }

            var current = ParseDecimal(row, "CurrentCtc", Error);
            var expected = ParseDecimal(row, "ExpectedCtc", Error);
            var experience = ParseDecimal(row, "ExperienceYears", Error);
            var notice = ParseDecimal(row, "NoticePeriodDays", Error);
            var lastContacted = ParseDate(row, "LastContacted", Error);
            if (errors.Count > before || country is null)
            {
                continue;
            }

            try
            {
                var person = new Person(row.Get("GivenName"), row.Get("FamilyName"));
                person.UpdateContact(email.Length == 0 ? null : email, row.Get("Phone"));
                var profile = new CandidateProfile(person.Id, country.CountryCode, now);
                profile.Update(
                    row.Get("CurrentEmployer"),
                    notice is { } days ? (int)days : null,
                    current is { } c ? Money(currency, c) : null,
                    expected is { } x ? Money(currency, x) : null,
                    currencies);
                profile.UpdateResumeDetails(country.CountryCode, row.Get("Location"), experience, Split(row.Get("Skills")), Split(row.Get("Tags")));
                profile.ApplyAutomaticStatus(status, StatusChangeSource.Import, "Imported", actor, now);
                if (lastContacted is { } date)
                {
                    profile.MarkContacted(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
                }

                var notes = row.Get("Notes");
                created.Add((person, profile, notes.Length > 0 ? new CandidateNote(person.Id, notes, actor, now) : null));
            }
            catch (DomainValidationException e)
            {
                Error(Column(e.Field), e.Message);
            }
        }

        return (errors.OrderBy(e => e.Row).ToList(), created);
    }

    private static Money Money(string currency, decimal amount)
    {
        try
        {
            return new Money(amount, currency);
        }
        catch (ArgumentException)
        {
            throw new DomainValidationException("currentCtc", "Use a three-letter currency code, PKR or USD.");
        }
    }

    private static string Column(string field) => field switch
    {
        "givenName" => "GivenName",
        "familyName" => "FamilyName",
        "email" => "Email",
        "phone" => "Phone",
        "currentCtc" => "CurrentCtc",
        "expectedCtc" => "ExpectedCtc",
        "noticePeriodDays" => "NoticePeriodDays",
        "experienceYears" => "ExperienceYears",
        "skills" => "Skills",
        "tags" => "Tags",
        "location" => "Location",
        "currentEmployer" => "CurrentEmployer",
        "body" => "Notes",
        _ => field,
    };

    private static IEnumerable<string> Split(string value) => value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static ResumeBankStatus? ParseStatus(SheetRow row, Action<string, string> error)
    {
        var value = row.Get("Status").Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (value.Length == 0)
        {
            return null;
        }

        if (Enum.TryParse<ResumeBankStatus>(value, ignoreCase: true, out var status) && Enum.IsDefined(status) && !int.TryParse(value, out _)
            && status != ResumeBankStatus.Blacklisted)
        {
            return status;
        }

        error("Status", "Use Shortlisted, Not shortlisted, No-show, Offer declined, Resigned, Terminated or Hired. Blacklisting is done in the portal by an HR Admin.");
        return null;
    }

    private static decimal? ParseDecimal(SheetRow row, string column, Action<string, string> error)
    {
        var value = row.Get(column).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        error(column, "Enter a number, e.g. 2400000.");
        return null;
    }

    private static DateOnly? ParseDate(SheetRow row, string column, Action<string, string> error)
    {
        var value = row.Get(column);
        if (value.Length == 0)
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        error(column, "Use the format YYYY-MM-DD (or an Excel date cell).");
        return null;
    }
}
