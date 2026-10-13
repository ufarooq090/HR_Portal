using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using HrPortal.Domain.Employees;
using HrPortal.Infrastructure.Employees;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.IntegrationTests.Employees;

/// <summary>EMP-07: Excel/CSV bulk import with validation and a downloadable error report.</summary>
[Collection(ApiCollection.Name)]
public class EmployeeImportTests(HrPortalApiFactory factory)
{
    private const string Header =
        "EmployeeNumber,GivenName,FamilyName,WorkEmail,Designation,EmploymentType,LegalEntity,Office,Department,Grade,ManagerEmployeeNumber,JoiningDate,Status,NationalId,AnnualCtc,SalaryBand";

    private static string Row(string number, string manager = "", string joining = "2024-01-15", string extra = ",,,", string department = "ENG",
        string entity = "HR Portal Dev (Pvt) Ltd", string office = "Lahore Office", string type = "Permanent", string grade = "G1") =>
        $"{number},Imported,Person {number},{number.ToLowerInvariant()}@import.test,Engineer,{type},{entity},{office},{department},{grade},{manager},{joining}{extra}";

    private static MultipartFormDataContent Csv(params string[] rows) => File(Encoding.UTF8.GetBytes(string.Join("\n", rows)), "employees.csv");

    private static MultipartFormDataContent File(byte[] bytes, string name)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { content, "file", name } };
    }

    private async Task<ImportResult> ImportAsync(MultipartFormDataContent form, string devUser = "dev-pk-hradmin")
    {
        var response = await factory.CreateClientAs(devUser).PostAsync("/api/employees/import", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportResult>())!;
    }

    [Fact]
    public async Task EMP_07_valid_csv_imports_every_row_with_managers_from_the_file_or_the_portal()
    {
        string a = TestEmployees.NewNumber(), b = TestEmployees.NewNumber(), c = TestEmployees.NewNumber();

        // B's manager (A) comes later in the file; A reports to an existing employee.
        var result = await ImportAsync(Csv(
            Header,
            Row(b, manager: a),
            Row(a, manager: "PK-0004"),
            Row(c, joining: "2025-09-01", extra: ",Probation,35202-7654321-9,\"2,400,000\",B1")));

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Imported);
        var imported = await factory.WithDbAsync(db => db.Employees.Include(e => e.Manager)
            .Where(e => e.EmployeeNumber == a || e.EmployeeNumber == b || e.EmployeeNumber == c)
            .ToDictionaryAsync(e => e.EmployeeNumber));
        Assert.Equal("PK-0004", imported[a].Manager!.EmployeeNumber);
        Assert.Equal(a, imported[b].Manager!.EmployeeNumber);
        Assert.Equal(EmploymentStatus.Confirmed, imported[a].Status);
        Assert.Equal(EmploymentStatus.Probation, imported[c].Status);
        Assert.Equal("PK", imported[c].CountryCode);

        var pay = await factory.WithDbAsync(db => db.Compensation.SingleAsync(x => x.EmployeeId == imported[c].Id));
        Assert.Equal(2_400_000m, pay.AnnualCtc.Amount);
        Assert.Equal("PKR", pay.CurrencyCode);
        Assert.Equal("B1", pay.SalaryBand);
    }

    [Fact]
    public async Task EMP_07_any_error_imports_nothing_and_lists_every_problem_by_row_and_column()
    {
        var valid = TestEmployees.NewNumber();
        var file = Csv(
            Header,
            Row(valid),
            Row(TestEmployees.NewNumber(), department: "NOPE"),
            Row("PK-0001"),
            Row(TestEmployees.NewNumber(), joining: "15/01/2024", type: "Freelance"),
            Row(TestEmployees.NewNumber(), manager: "PK-9999"),
            Row(TestEmployees.NewNumber(), office: "US Office"));

        var result = await ImportAsync(file);

        Assert.Equal(0, result.Imported);
        Assert.Equal(6, result.TotalRows);
        Assert.Contains(result.Errors, e => e is { Row: 3, Column: "Department" });
        Assert.Contains(result.Errors, e => e is { Row: 4, Column: "EmployeeNumber" });
        Assert.Contains(result.Errors, e => e is { Row: 5, Column: "JoiningDate" });
        Assert.Contains(result.Errors, e => e is { Row: 5, Column: "EmploymentType" });
        Assert.Contains(result.Errors, e => e is { Row: 6, Column: "ManagerEmployeeNumber" });
        Assert.Contains(result.Errors, e => e is { Row: 7, Column: "Office" });
        Assert.DoesNotContain(result.Errors, e => e.Row == 2);
        Assert.False(await factory.WithDbAsync(db => db.Employees.AnyAsync(e => e.EmployeeNumber == valid)));
    }

    [Fact]
    public async Task EMP_07_duplicates_within_the_file_and_reporting_loops_are_reported()
    {
        string a = TestEmployees.NewNumber(), b = TestEmployees.NewNumber();

        var result = await ImportAsync(Csv(Header, Row(a, manager: b), Row(b, manager: a), Row(a)));

        Assert.Contains(result.Errors, e => e is { Row: 4, Column: "EmployeeNumber" } && e.Message.Contains("row 2"));
        Assert.Contains(result.Errors, e => e.Column == "ManagerEmployeeNumber" && e.Message.Contains("loop"));
    }

    [Fact]
    public async Task EMP_07_missing_required_columns_are_reported_once()
    {
        var result = await ImportAsync(Csv("EmployeeNumber,GivenName", "X1,Ali"));

        var error = Assert.Single(result.Errors);
        Assert.Equal(1, error.Row);
        Assert.Contains("WorkEmail", error.Column);
    }

    [Fact]
    public async Task EMP_07_error_report_returns_the_rows_with_an_errors_column()
    {
        string good = TestEmployees.NewNumber(), bad = TestEmployees.NewNumber();
        var rows = new[] { Header, Row(good), Row(bad, department: "NOPE") };

        var response = await factory.CreateClientAs("dev-pk-hradmin").PostAsync("/api/employees/import/error-report", Csv(rows));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("employee-import-errors.xlsx", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName);
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet("Employees");
        var errorsColumn = sheet.Row(1).CellsUsed().Single(c => c.GetString() == "Errors").Address.ColumnNumber;
        Assert.Equal(good, sheet.Cell(2, 1).GetString());
        Assert.Equal(string.Empty, sheet.Cell(2, errorsColumn).GetString());
        Assert.Contains("Department", sheet.Cell(3, errorsColumn).GetString());
        Assert.Equal(3, workbook.Worksheet("Error list").Cell(2, 1).GetValue<int>());
    }

    [Fact]
    public async Task EMP_07_xlsx_with_excel_dates_and_numbers_imports()
    {
        var number = TestEmployees.NewNumber();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Employees");
        var headers = Header.Split(',');
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        sheet.Cell(2, 1).Value = number;
        sheet.Cell(2, 2).Value = "Excel";
        sheet.Cell(2, 3).Value = "Import";
        sheet.Cell(2, 4).Value = $"{number.ToLowerInvariant()}@import.test";
        sheet.Cell(2, 5).Value = "Analyst";
        sheet.Cell(2, 6).Value = "contract";
        sheet.Cell(2, 7).Value = "HR Portal Dev Inc.";
        sheet.Cell(2, 8).Value = "US Office";
        sheet.Cell(2, 9).Value = "Finance";
        sheet.Cell(2, 12).Value = new DateTime(2023, 3, 20);
        sheet.Cell(2, 15).Value = 85000.5;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var result = await ImportAsync(File(stream.ToArray(), "employees.xlsx"), "dev-us-hradmin");

        Assert.Empty(result.Errors);
        var employee = await factory.WithDbAsync(db => db.Employees.SingleAsync(e => e.EmployeeNumber == number));
        Assert.Equal(new DateOnly(2023, 3, 20), employee.JoiningDate);
        Assert.Equal(EmploymentType.Contract, employee.EmploymentType);
        Assert.Equal("US", employee.CountryCode);
        Assert.Equal(85000.5m, (await factory.WithDbAsync(db => db.Compensation.SingleAsync(c => c.EmployeeId == employee.Id))).AnnualCtc.Amount);
    }

    [Fact]
    public async Task MC_03_rows_outside_the_importers_countries_are_refused()
    {
        var result = await ImportAsync(
            Csv(Header, Row(TestEmployees.NewNumber(), entity: "HR Portal Dev Inc.", office: "US Office")), "dev-pk-hrexecutive");

        var error = Assert.Single(result.Errors);
        Assert.Equal("LegalEntity", error.Column);
        Assert.Contains("United States", error.Message);
    }

    [Fact]
    public async Task EMP_03_salary_columns_need_compensation_access()
    {
        var result = await ImportAsync(Csv(Header, Row(TestEmployees.NewNumber(), extra: ",,,1000000,")), "dev-pk-hrexecutive");

        Assert.Equal("AnnualCtc", Assert.Single(result.Errors).Column);
    }

    [Fact]
    public async Task Unsupported_file_type_returns_400()
    {
        var response = await factory.CreateClientAs("dev-pk-hradmin").PostAsync("/api/employees/import", File("%PDF"u8.ToArray(), "employees.pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(EmployeeRecordsTests.NonHrUsers), MemberType = typeof(EmployeeRecordsTests))]
    public async Task NFR_02_only_HR_can_import(string devUser)
    {
        var client = factory.CreateClientAs(devUser);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/employees/import/template")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/employees/import", Csv(Header))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/employees/import/error-report", Csv(Header))).StatusCode);
    }

    [Fact]
    public async Task EMP_07_template_has_the_expected_headers_and_valid_values()
    {
        var bytes = await factory.CreateClientAs("dev-pk-hrexecutive").GetByteArrayAsync("/api/employees/import/template");

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(
            EmployeeImporter.Columns.Select(c => c.Name),
            workbook.Worksheet("Employees").Row(1).CellsUsed().Select(c => c.GetString()));
        Assert.Contains(workbook.Worksheet("Instructions").CellsUsed(), c => c.GetString() == "Lahore Office (PK)");
    }
}
