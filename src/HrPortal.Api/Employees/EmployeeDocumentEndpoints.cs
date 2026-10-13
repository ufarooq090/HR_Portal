using HrPortal.Api.Auth;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Files;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Employees;

/// <summary>
/// EMP-06: ID proofs, certificates, contracts and signed letters, stored via <see cref="IFileStore"/> and only ever
/// served through this API. Confidential: HR within their countries and the employee; never managers.
/// </summary>
public static class EmployeeDocumentEndpoints
{
    public sealed record DocumentDto(
        Guid Id, DocumentCategory Category, string FileName, string ContentType, long SizeBytes, DateOnly? ExpiryDate, DateTimeOffset UploadedAtUtc);

    public static IEndpointRouteBuilder MapEmployeeDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/employees/{id:guid}/documents").WithTags("Employees");

        group.MapGet("/", async (Guid id, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct) =>
            {
                var access = await accessContext.GetAsync(ct);
                if (await EmployeeScope.DenyAsync(db, access, id, access.ConfidentialDataScopesFor(Capability.View), ct) is { } denied)
                {
                    return denied;
                }

                return Results.Ok(await db.EmployeeDocuments.AsNoTracking()
                    .Where(d => d.EmployeeId == id)
                    .OrderByDescending(d => d.UploadedAtUtc)
                    .Select(d => new DocumentDto(d.Id, d.Category, d.FileName, d.ContentType, d.SizeBytes, d.ExpiryDate, d.UploadedAtUtc))
                    .ToListAsync(ct));
            })
            .RequireAuthorization(Policies.Module(HrModule.EmployeeRecords, Capability.View));

        // Bearer-token API with no cookies, so cross-site request forgery does not apply to form posts.
        group.MapPost("/", async (
                Guid id,
                IFormFile? file,
                [FromForm] DocumentCategory category,
                [FromForm] DateOnly? expiryDate,
                HrPortalDbContext db,
                IAccessContext accessContext,
                IFileStore files,
                TimeProvider clock,
                CancellationToken ct) =>
            {
                var access = await accessContext.GetAsync(ct);
                if (await EmployeeScope.DenyAsync(db, access, id, access.ConfidentialDataScopesFor(Capability.Edit), ct) is { } denied)
                {
                    return denied;
                }

                if (file is null)
                {
                    throw new DomainValidationException("file", "Choose a file to upload.");
                }

                await using var content = new MemoryStream();
                await file.CopyToAsync(content, ct);
                var upload = UploadPolicy.Validate(file.FileName, content.Length, content.GetBuffer().AsSpan(0, (int)Math.Min(content.Length, UploadPolicy.HeaderLength)));
                content.Position = 0;

                var key = await files.SaveAsync(EmployeeDocument.Container, upload.FileName, upload.ContentType, content, ct);
                var document = new EmployeeDocument(id, category, upload, key, expiryDate, clock.GetUtcNow());
                db.EmployeeDocuments.Add(document);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch
                {
                    await files.DeleteAsync(EmployeeDocument.Container, key, CancellationToken.None);
                    throw;
                }

                return Results.Created(
                    $"/api/employees/{id}/documents/{document.Id}",
                    new DocumentDto(document.Id, document.Category, document.FileName, document.ContentType, document.SizeBytes, document.ExpiryDate, document.UploadedAtUtc));
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadPolicy.MaxBytes + 64 * 1024))
            .RequireAuthorization(Policies.Module(HrModule.EmployeeRecords, Capability.Edit));

        group.MapGet("/{documentId:guid}/content", async (
                Guid id, Guid documentId, HrPortalDbContext db, IAccessContext accessContext, IFileStore files, IAuditLog audit, CancellationToken ct) =>
            {
                var access = await accessContext.GetAsync(ct);
                if (await EmployeeScope.DenyAsync(db, access, id, access.ConfidentialDataScopesFor(Capability.View), ct) is { } denied)
                {
                    return denied;
                }

                var document = await db.EmployeeDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == id, ct);
                if (document is null)
                {
                    return Results.NotFound();
                }

                await audit.RecordViewAsync(nameof(EmployeeDocument), document.Id.ToString(), ct);
                var stream = await files.OpenReadAsync(EmployeeDocument.Container, document.StorageKey, ct);
                return Results.File(stream, document.ContentType, document.FileName);
            })
            .RequireAuthorization(Policies.Module(HrModule.EmployeeRecords, Capability.View));

        group.MapDelete("/{documentId:guid}", async (
                Guid id, Guid documentId, HrPortalDbContext db, IAccessContext accessContext, IFileStore files, CancellationToken ct) =>
            {
                var access = await accessContext.GetAsync(ct);
                if (await EmployeeScope.DenyAsync(db, access, id, access.ConfidentialDataScopesFor(Capability.Delete), ct) is { } denied)
                {
                    return denied;
                }

                var document = await db.EmployeeDocuments.SingleOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == id, ct);
                if (document is null)
                {
                    return Results.NotFound();
                }

                db.EmployeeDocuments.Remove(document);
                await db.SaveChangesAsync(ct);
                await files.DeleteAsync(EmployeeDocument.Container, document.StorageKey, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Module(HrModule.EmployeeRecords, Capability.Delete));

        return app;
    }
}
