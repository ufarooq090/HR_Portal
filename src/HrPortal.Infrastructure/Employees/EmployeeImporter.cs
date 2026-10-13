using System.Globalization;
using ClosedXML.Excel;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Infrastructure.Employees;

public sealed record ImportColumn(string Name, bool Required, string Help);

/// <param name="Row">Spreadsheet row; 1 means the header or the file as a whole.</param>
public sealed record ImportError(int Row, string Column, string Message);

public sealed record ImportResult(int TotalRows, int Imported, IReadOnlyList<ImportError> Errors);

/// <summary>
/// EMP-07: bulk import of existing employees from Excel or CSV. Every row is validated first and nothing is saved
/// unless the whole file is valid, so HR can fix the error report and upload the same file again.
/// Rows are limited to the importer's HR countries (MC-03); salaries need compensation access (EMP-03).
/// </summary>
public sealed class EmployeeImporter(HrPortalDbContext db, TimeProvider clock)
{
    public const int MaxRows = 2000;

    public static readonly IReadOnlyList<ImportColumn> Columns =
    [
        new("EmployeeNumber", true, "Unique company employee ID, e.g. PK-0042."),
        new("GivenName", true, "First name."),
        new("FamilyName", true, "Last name."),
        new("WorkEmail", true, "Company email; must be unique."),
        new("Designation", true, "Job title, e.g. Software Engineer."),
        new("EmploymentType", true, "Permanent, Contract or Intern."),
        new("LegalEntity", true, "Legal entity name exactly as in Settings > Legal entities. Sets the country."),
        new("Office", true, "Office name in the legal entity's country (Settings > Offices)."),
        new("Department", true, "Department code or name (Settings > Departments)."),
        new("Grade", false, "Grade code in the employee's country (Settings > Grades)."),
        new("ManagerEmployeeNumber", false, "Employee ID of the reporting manager: an existing employee or another row in this file."),
        new("JoiningDate", true, "Date as YYYY-MM-DD, or an Excel date cell."),
        new("Status", false, "Probation or Confirmed. Empty: Confirmed if probation has already ended."),
        new("PersonalEmail", false, "Personal email."),
        new("Phone", false, "Personal phone number. Format the column as text in Excel to keep leading zeros."),
        new("DateOfBirth", false, "Date as YYYY-MM-DD, or an Excel date cell."),
        new("NationalId", false, "CNIC (Pakistan) or SSN (US). Stored encrypted."),
        new("AnnualCtc", false, "Annual cost to company in the country's currency (PKR or USD). Needs compensation access."),
        new("SalaryBand", false, "Salary band label. Needs compensation access."),
    ];

    private static readonly Dictionary<string, string> FieldToColumn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["employeeNumber"] = "EmployeeNumber",
        ["givenName"] = "GivenName",
        ["familyName"] = "FamilyName",
        ["workEmail"] = "WorkEmail",
        ["designation"] = "Designation",
        ["officeLocationId"] = "Office",
        ["gradeId"] = "Grade",
        ["managerId"] = "ManagerEmployeeNumber",
        ["personalEmail"] = "PersonalEmail",
        ["phone"] = "Phone",
        ["dateOfBirth"] = "DateOfBirth",
        ["nationalId"] = "NationalId",
        ["annualCtc"] = "AnnualCtc",
        ["currencyCode"] = "AnnualCtc",
        ["salaryBand"] = "SalaryBand",
        ["effectiveDate"] = "Status",
        ["status"] = "Status",
    };

    public async Task<ImportResult> ImportAsync(Sheet sheet, UserAccess access, CancellationToken ct)
    {
        var (errors, employees, compensation) = await ValidateAsync(sheet, access, ct);
        if (errors.Count > 0)
        {
            return new ImportResult(sheet.Rows.Count, 0, errors);
        }

        db.People.AddRange(employees.Select(e => e.Person));
        db.Employees.AddRange(employees);
        db.Compensation.AddRange(compensation);
        await db.SaveChangesAsync(ct);
        return new ImportResult(sheet.Rows.Count, employees.Count, []);
    }

    /// <summary>The uploaded rows with an Errors column, plus a list of every error, as .xlsx.</summary>
    public async Task<byte[]> ErrorReportAsync(Sheet sheet, UserAccess access, CancellationToken ct)
    {
        var (errors, _, _) = await ValidateAsync(sheet, access, ct);
        db.ChangeTracker.Clear();

        using var workbook = new XLWorkbook();
        var data = workbook.AddWorksheet("Employees");
        var headers = sheet.Headers.Where(h => SpreadsheetReader.Key(h) != "errors").ToList();
        for (var c = 0; c < headers.Count; c++)
        {
            data.Cell(1, c + 1).Value = headers[c];
        }

        data.Cell(1, headers.Count + 1).Value = "Errors";
        data.Row(1).Style.Font.Bold = true;

        var byRow = errors.GroupBy(e => e.Row).ToDictionary(g => g.Key, g => string.Join("; ", g.Select(e => $"{e.Column}: {e.Message}")));
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            for (var c = 0; c < headers.Count; c++)
            {
                // Text cells, so IDs and phone numbers keep their leading zeros on re-upload.
                data.Cell(r + 2, c + 1).SetValue(row.Get(headers[c]));
            }

            if (byRow.TryGetValue(row.RowNumber, out var message))
            {
                data.Cell(r + 2, headers.Count + 1).Value = message;
                data.Row(r + 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDE7E9");
            }
        }

        data.SheetView.FreezeRows(1);
        data.Columns().AdjustToContents(1, Math.Min(sheet.Rows.Count + 1, 200), 8, 60);

        var list = workbook.AddWorksheet("Error list");
        list.Cell(1, 1).Value = "Row";
        list.Cell(1, 2).Value = "Column";
        list.Cell(1, 3).Value = "Problem";
        list.Row(1).Style.Font.Bold = true;
        for (var i = 0; i < errors.Count; i++)
        {
            list.Cell(i + 2, 1).Value = errors[i].Row;
            list.Cell(i + 2, 2).Value = errors[i].Column;
            list.Cell(i + 2, 3).Value = errors[i].Message;
        }

        list.Columns().AdjustToContents();
        return ToBytes(workbook);
    }

    /// <summary>An empty Employees sheet with the expected headers, and instructions listing valid values.</summary>
    public async Task<byte[]> TemplateAsync(CancellationToken ct)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Employees");
        for (var c = 0; c < Columns.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = Columns[c].Name;
            sheet.Column(c + 1).Style.NumberFormat.Format = "@";
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();

        var help = workbook.AddWorksheet("Instructions");
        help.Cell(1, 1).Value = "Column";
        help.Cell(1, 2).Value = "Required";
        help.Cell(1, 3).Value = "What to enter";
        for (var i = 0; i < Columns.Count; i++)
        {
            help.Cell(i + 2, 1).Value = Columns[i].Name;
            help.Cell(i + 2, 2).Value = Columns[i].Required ? "Yes" : "No";
            help.Cell(i + 2, 3).Value = Columns[i].Help;
        }

        var row = Columns.Count + 3;
        void List(string title, IEnumerable<string> values)
        {
            help.Cell(row++, 1).Value = title;
            foreach (var value in values)
            {
                help.Cell(row++, 3).Value = value;
            }

            row++;
        }

        List("Legal entities", await db.LegalEntities.Where(e => e.IsActive).OrderBy(e => e.Name).Select(e => $"{e.Name} ({e.CountryCode})").ToListAsync(ct));
        List("Offices", await db.OfficeLocations.Where(e => e.IsActive).OrderBy(e => e.Name).Select(e => $"{e.Name} ({e.CountryCode})").ToListAsync(ct));
        List("Departments", await db.Departments.Where(e => e.IsActive).OrderBy(e => e.Code).Select(e => $"{e.Code} – {e.Name}").ToListAsync(ct));
        List("Grades", await db.Grades.Where(e => e.IsActive).OrderBy(e => e.CountryCode).ThenBy(e => e.Level).Select(e => $"{e.Code} – {e.Name} ({e.CountryCode})").ToListAsync(ct));

        help.Row(1).Style.Font.Bold = true;
        help.Column(1).Style.Font.Bold = true;
        help.Columns().AdjustToContents();
        return ToBytes(workbook);
    }

    private async Task<(List<ImportError> Errors, List<Employee> Employees, List<Compensation> Compensation)> ValidateAsync(
        Sheet sheet, UserAccess access, CancellationToken ct)
    {
        var errors = new List<ImportError>();
        var missing = Columns.Where(c => c.Required && !sheet.HasColumn(c.Name)).Select(c => c.Name).ToList();
        if (missing.Count > 0)
        {
            errors.Add(new(1, string.Join(", ", missing), "Required column missing. Download the template for the expected headers."));
            return (errors, [], []);
        }

        if (sheet.Rows.Count == 0)
        {
            errors.Add(new(1, string.Empty, "The file has no employee rows."));
            return (errors, [], []);
        }

        if (sheet.Rows.Count > MaxRows)
        {
            errors.Add(new(1, string.Empty, $"Import at most {MaxRows} rows at a time."));
            return (errors, [], []);
        }

        var lookups = await LoadLookupsAsync(sheet, ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var canCreateIn = access.ScopesFor(HrModule.EmployeeRecords, Capability.Create).Contains(DataScope.Countries);
        var canPayIn = access.ScopesFor(HrModule.Compensation, Capability.Create).Contains(DataScope.Countries);

        var drafts = new List<Draft>();
        var seenNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenEmails = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in sheet.Rows)
        {
            void Error(string column, string message) => errors.Add(new(row.RowNumber, column, message));

            foreach (var column in Columns.Where(c => c.Required && row.Get(c.Name).Length == 0))
            {
                Error(column.Name, "Required.");
            }

            var number = row.Get("EmployeeNumber").ToUpperInvariant();
            if (number.Length > 0 && (lookups.ExistingNumbers.Contains(number) || !seenNumbers.TryAdd(number, row.RowNumber)))
            {
                Error("EmployeeNumber", lookups.ExistingNumbers.Contains(number) ? "An employee with this ID already exists." : $"Also used on row {seenNumbers[number]}.");
            }

            var email = row.Get("WorkEmail").ToLowerInvariant();
            if (email.Length > 0 && (lookups.ExistingEmails.Contains(email) || !seenEmails.TryAdd(email, row.RowNumber)))
            {
                Error("WorkEmail", lookups.ExistingEmails.Contains(email) ? "An employee with this email already exists." : $"Also used on row {seenEmails[email]}.");
            }

            var legalEntity = Lookup(row, "LegalEntity", lookups.LegalEntities, e => [e.Name], Error);
            var country = legalEntity is null ? null : lookups.Countries[legalEntity.CountryCode];
            if (country is not null && !(canCreateIn && access.HrCountries.Includes(country.CountryCode)))
            {
                Error("LegalEntity", $"You cannot add employees in {country.Name}.");
                country = null;
            }

            var office = Lookup(row, "Office", lookups.Offices.Where(o => o.CountryCode == legalEntity?.CountryCode), o => [o.Name], Error);
            var department = Lookup(row, "Department", lookups.Departments, d => [d.Code, d.Name], Error);
            var grade = row.Get("Grade").Length == 0
                ? null
                : Lookup(row, "Grade", lookups.Grades.Where(g => g.CountryCode == legalEntity?.CountryCode), g => [g.Code], Error);
            var type = ParseEnum<EmploymentType>(row, "EmploymentType", Error);
            var joining = ParseDate(row, "JoiningDate", Error);
            var dateOfBirth = ParseDate(row, "DateOfBirth", Error);
            var status = ParseEnum<EmploymentStatus>(row, "Status", Error);
            if (status is not (null or EmploymentStatus.Probation or EmploymentStatus.Confirmed))
            {
                Error("Status", "Import current employees as Probation or Confirmed; record other statuses afterwards.");
            }

            var ctc = ParseAmount(row, "AnnualCtc", Error);
            if ((ctc is not null || row.Get("SalaryBand").Length > 0) && !canPayIn)
            {
                Error("AnnualCtc", "You do not have access to compensation. Remove the AnnualCtc and SalaryBand columns or ask an HR Admin.");
                ctc = null;
            }

            drafts.Add(new Draft(row, number, legalEntity, office, department, grade, type, joining, dateOfBirth, status, ctc, country));
        }

        // Managers may be other rows, so create employees manager-first.
        var byNumber = drafts.Where(d => d.Number.Length > 0).GroupBy(d => d.Number).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var created = new Dictionary<Draft, Employee?>();
        var employees = new List<Employee>();
        var compensation = new List<Compensation>();

        Employee? Build(Draft draft, HashSet<Draft> path)
        {
            if (created.TryGetValue(draft, out var done))
            {
                return done;
            }

            void Error(string column, string message) => errors.Add(new(draft.Row.RowNumber, column, message));
            Employee? manager = null;
            var managerNumber = draft.Row.Get("ManagerEmployeeNumber").ToUpperInvariant();
            if (managerNumber.Length > 0)
            {
                if (byNumber.TryGetValue(managerNumber, out var managerDraft))
                {
                    if (!path.Add(managerDraft))
                    {
                        Error("ManagerEmployeeNumber", "Managers in this file form a reporting loop.");
                    }
                    else
                    {
                        manager = Build(managerDraft, path);
                        path.Remove(managerDraft);
                    }
                }
                else if (!lookups.Managers.TryGetValue(managerNumber, out manager))
                {
                    Error("ManagerEmployeeNumber", "No employee with this ID exists in the portal or in this file.");
                }
            }

            created[draft] = null;
            if (draft is not { LegalEntity: { } le, Office: { } office, Department: { } dept, Type: { } type, Joining: { } joining, Country: { } country })
            {
                return null;
            }

            try
            {
                var person = new Person(draft.Row.Get("GivenName"), draft.Row.Get("FamilyName"));
                person.UpdatePersonalDetails(
                    PersonalDetails.Empty with
                    {
                        PersonalEmail = draft.Row.Get("PersonalEmail"),
                        Phone = draft.Row.Get("Phone"),
                        DateOfBirth = draft.DateOfBirth,
                        NationalId = draft.Row.Get("NationalId"),
                    },
                    today);

                var employee = Employee.Create(
                    person, draft.Number, draft.Row.Get("WorkEmail"), type,
                    new JobPlacement(draft.Row.Get("Designation"), le, office, dept, draft.Grade),
                    manager, joining, EmploymentTerms.For(country, draft.Grade));

                var confirm = draft.Status == EmploymentStatus.Confirmed
                    || (draft.Status is null && employee.ProbationEndDate is { } end && end <= today);
                if (confirm && employee.Status == EmploymentStatus.Probation)
                {
                    var probationEnd = employee.ProbationEndDate!.Value;
                    employee.ChangeStatus(
                        EmploymentStatus.Confirmed, probationEnd < today ? probationEnd : today, null,
                        EmploymentTerms.For(country, draft.Grade), "Imported", today);
                }

                if (draft.AnnualCtc is { } amount)
                {
                    compensation.Add(Compensation.Create(
                        employee, country, new Money(amount, country.CurrencyCode), draft.Row.Get("SalaryBand"),
                        joining, CompensationReason.Joining, "Imported"));
                }

                created[draft] = employee;
                employees.Add(employee);
                return employee;
            }
            catch (DomainValidationException e)
            {
                Error(FieldToColumn.GetValueOrDefault(e.Field, e.Field), e.Message);
                return null;
            }
        }

        foreach (var draft in drafts)
        {
            Build(draft, [draft]);
        }

        return (errors.OrderBy(e => e.Row).ToList(), employees, compensation);
    }

    private async Task<Lookups> LoadLookupsAsync(Sheet sheet, CancellationToken ct)
    {
        var numbers = sheet.Rows.Select(r => r.Get("EmployeeNumber").ToUpperInvariant()).Where(n => n.Length > 0).ToList();
        var emails = sheet.Rows.Select(r => r.Get("WorkEmail").ToLowerInvariant()).Where(n => n.Length > 0).ToList();
        var managerNumbers = sheet.Rows.Select(r => r.Get("ManagerEmployeeNumber").ToUpperInvariant()).Where(n => n.Length > 0).Distinct().ToList();

        return new Lookups(
            await db.CountrySettings.ToDictionaryAsync(c => c.CountryCode, ct),
            await db.LegalEntities.Where(e => e.IsActive).ToListAsync(ct),
            await db.OfficeLocations.Where(e => e.IsActive).ToListAsync(ct),
            await db.Departments.Where(e => e.IsActive).ToListAsync(ct),
            await db.Grades.Where(e => e.IsActive).ToListAsync(ct),
            (await db.Employees.Where(e => numbers.Contains(e.EmployeeNumber)).Select(e => e.EmployeeNumber).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase),
            (await db.Employees.Where(e => emails.Contains(e.WorkEmail)).Select(e => e.WorkEmail).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase),
            await db.Employees
                .Where(e => managerNumbers.Contains(e.EmployeeNumber)
                    && (e.Status == EmploymentStatus.Probation || e.Status == EmploymentStatus.Confirmed || e.Status == EmploymentStatus.OnNotice))
                .ToDictionaryAsync(e => e.EmployeeNumber, StringComparer.OrdinalIgnoreCase, ct));
    }

    private static T? Lookup<T>(SheetRow row, string column, IEnumerable<T> items, Func<T, string[]> names, Action<string, string> error)
        where T : class
    {
        var value = row.Get(column);
        if (value.Length == 0)
        {
            return null;
        }

        var match = items.FirstOrDefault(i => names(i).Any(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase)));
        if (match is null)
        {
            error(column, $"'{value}' is not an active {column.ToLowerInvariant()}" + (column is "Office" or "Grade" ? " in the legal entity's country." : "."));
        }

        return match;
    }

    private static TEnum? ParseEnum<TEnum>(SheetRow row, string column, Action<string, string> error)
        where TEnum : struct, Enum
    {
        var value = row.Get(column).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (value.Length == 0)
        {
            return null;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _))
        {
            return parsed;
        }

        error(column, $"Use one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
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

        // Day/month order differs between Pakistan and the US, so only unambiguous ISO dates are accepted.
        error(column, "Use the format YYYY-MM-DD (or an Excel date cell).");
        return null;
    }

    private static decimal? ParseAmount(SheetRow row, string column, Action<string, string> error)
    {
        var value = row.Get(column).Replace(",", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (value.Length == 0)
        {
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return amount;
        }

        error(column, "Enter a number without a currency symbol, e.g. 2400000.");
        return null;
    }

    private static byte[] ToBytes(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed record Draft(
        SheetRow Row,
        string Number,
        LegalEntity? LegalEntity,
        OfficeLocation? Office,
        Department? Department,
        Grade? Grade,
        EmploymentType? Type,
        DateOnly? Joining,
        DateOnly? DateOfBirth,
        EmploymentStatus? Status,
        decimal? AnnualCtc,
        CountrySettings? Country);

    private sealed record Lookups(
        Dictionary<string, CountrySettings> Countries,
        List<LegalEntity> LegalEntities,
        List<OfficeLocation> Offices,
        List<Department> Departments,
        List<Grade> Grades,
        HashSet<string> ExistingNumbers,
        HashSet<string> ExistingEmails,
        Dictionary<string, Employee> Managers);
}
