using HrPortal.Api.Auth;
using HrPortal.Domain.Common;
using HrPortal.Domain.Files;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Employees;
using Microsoft.AspNetCore.Mvc;

namespace HrPortal.Api.Employees;

/// <summary>
/// EMP-07: Excel/CSV import. The import is all-or-nothing; when it fails the client posts the same file to
/// /error-report to download it annotated with the problems. Nothing is stored between the two calls.
/// </summary>
public static class EmployeeImportEndpoints
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapEmployeeImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/employees/import").WithTags("Employees")
            .RequireAuthorization(Policies.Module(HrModule.EmployeeRecords, Capability.Create));

        group.MapGet("/template", async (EmployeeImporter importer, CancellationToken ct) =>
            Results.File(await importer.TemplateAsync(ct), XlsxContentType, "employee-import-template.xlsx"));

        // Bearer-token API with no cookies, so cross-site request forgery does not apply to form posts.
        group.MapPost("/", async (IFormFile? file, EmployeeImporter importer, IAccessContext accessContext, CancellationToken ct) =>
            {
                var sheet = await ReadAsync(file, ct);
                return Results.Ok(await importer.ImportAsync(sheet, await accessContext.GetAsync(ct), ct));
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadPolicy.MaxBytes + 64 * 1024));

        group.MapPost("/error-report", async (IFormFile? file, EmployeeImporter importer, IAccessContext accessContext, CancellationToken ct) =>
            {
                var sheet = await ReadAsync(file, ct);
                var report = await importer.ErrorReportAsync(sheet, await accessContext.GetAsync(ct), ct);
                return Results.File(report, XlsxContentType, "employee-import-errors.xlsx");
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadPolicy.MaxBytes + 64 * 1024));

        return app;
    }

    private static async Task<Sheet> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new DomainValidationException("file", "Choose an .xlsx or .csv file.");
        }

        if (file.Length > UploadPolicy.MaxBytes)
        {
            throw new DomainValidationException("file", "Files can be at most 10 MB.");
        }

        await using var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        content.Position = 0;
        return SpreadsheetReader.Read(content, file.FileName);
    }
}
