using HrPortal.Api.Auth;
using HrPortal.Domain.Common;
using HrPortal.Domain.Files;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;
using HrPortal.Domain.Templates;
using HrPortal.Infrastructure.Persistence;
using HrPortal.Infrastructure.Templates;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Templates;

/// <summary>
/// Sending emails from templates (ET-02..05) and the email history on a person's record (ET-08). Emails go
/// through IEmailSender (Mailpit locally); automatic sending (ET-07) and Outlook come in Phase 2.
/// </summary>
public static class EmailEndpoints
{
    public sealed record ComposeRequest(Guid TemplateId, Guid RecordId, string? Subject, string? BodyHtml);

    public sealed record PreviewResponse(
        Guid TemplateId,
        Guid RecordId,
        string RecipientName,
        string? To,
        IReadOnlyList<string> Cc,
        string Subject,
        string BodyHtml,
        IReadOnlyList<MergeField> MissingFields,
        IReadOnlyList<string> Problems,
        bool CanSend,
        SenderMailbox DefaultSender);

    public sealed record SendRequest(Guid TemplateId, Guid RecordId, string? Subject, string? BodyHtml, SenderMailbox Sender, IReadOnlyList<Guid>? AttachmentIds);

    public sealed record BulkRequest(Guid TemplateId, IReadOnlyList<Guid> RecordIds, SenderMailbox Sender, IReadOnlyList<Guid>? AttachmentIds);

    public sealed record TestResponse(string SentTo);

    public sealed record AttachmentDto(Guid Id, string FileName, long SizeBytes);

    public sealed record EmailLogDto(
        Guid Id,
        TemplateRecordType RecordType,
        Guid RecordId,
        string? TemplateName,
        int? TemplateVersion,
        TemplateCategory? Category,
        string To,
        IReadOnlyList<string> Cc,
        SenderMailbox Sender,
        string FromAddress,
        string Subject,
        string BodyHtml,
        IReadOnlyList<string> Attachments,
        Guid? BatchId,
        string SentByName,
        DateTimeOffset SentAtUtc,
        EmailStatus Status,
        string? Error);

    public static IEndpointRouteBuilder MapEmailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/emails").WithTags("Emails");
        var use = Policies.Module(HrModule.EmailTemplates, Capability.Use);

        group.MapPost("/preview", PreviewAsync).RequireAuthorization(use);
        group.MapPost("/test", TestAsync).RequireAuthorization(use);
        group.MapPost("/send", SendAsync).RequireAuthorization(use);
        group.MapPost("/bulk", BulkAsync).RequireAuthorization(use);
        group.MapPost("/attachments", UploadAsync).RequireAuthorization(use)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadPolicy.MaxBytes + 64 * 1024));
        app.MapGet("/api/people/{personId:guid}/emails", HistoryAsync).WithTags("Emails").RequireAuthorization(Policies.Authenticated);
        app.MapGet("/api/employees/{id:guid}/emails", async (Guid id, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct) =>
                await db.Employees.Where(e => e.Id == id).Select(e => (Guid?)e.PersonId).SingleOrDefaultAsync(ct) is { } personId
                    ? await HistoryAsync(personId, db, accessContext, ct)
                    : Results.NotFound())
            .WithTags("Emails")
            .RequireAuthorization(Policies.Authenticated);
        return app;
    }

    /// <summary>ET-03: the email filled with the record's real data, with anything that would block sending.</summary>
    private static async Task<IResult> PreviewAsync(ComposeRequest request, EmailService emails, IAccessContext accessContext, ICurrentUser user, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var template = await emails.TemplateAsync(request.TemplateId, ct);
        var email = await emails.ComposeAsync(access, ActorOf(user, access), template, request.RecordId, bulk: false, request.Subject, request.BodyHtml, ct);
        return Results.Ok(new PreviewResponse(
            template.Id, request.RecordId, email.Record.RecipientName, email.To, email.Cc, email.Subject, email.BodyHtml, email.Missing, email.Problems,
            email.CanSend, template.DefaultSender));
    }

    private static async Task<IResult> TestAsync(SendRequest request, EmailService emails, IAccessContext accessContext, ICurrentUser user, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var to = await emails.SendTestAsync(
            access, ActorOf(user, access), request.TemplateId, request.RecordId, request.Subject, request.BodyHtml, request.Sender, request.AttachmentIds ?? [], ct);
        return Results.Ok(new TestResponse(to));
    }

    private static async Task<IResult> SendAsync(SendRequest request, EmailService emails, IAccessContext accessContext, ICurrentUser user, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var log = await emails.SendAsync(
            access, ActorOf(user, access), request.TemplateId, request.RecordId, request.Subject, request.BodyHtml, request.Sender, request.AttachmentIds ?? [], ct);
        return Results.Ok(Dto(log));
    }

    private static async Task<IResult> BulkAsync(BulkRequest request, EmailService emails, IAccessContext accessContext, ICurrentUser user, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        return Results.Ok(await emails.SendBulkAsync(
            access, ActorOf(user, access), request.TemplateId, request.RecordIds ?? [], request.Sender, request.AttachmentIds ?? [], ct));
    }

    private static async Task<IResult> UploadAsync(IFormFile? file, EmailService emails, IAccessContext accessContext, ICurrentUser user, CancellationToken ct)
    {
        if (file is null)
        {
            throw new DomainValidationException("file", "Choose a file to attach.");
        }

        var access = await accessContext.GetAsync(ct);
        await using var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        var upload = UploadPolicy.Validate(file.FileName, file.Length, content.ToArray().AsSpan(0, (int)Math.Min(content.Length, 16)));
        content.Position = 0;
        var saved = await emails.UploadAsync(upload, content, ActorOf(user, access), ct);
        return Results.Ok(new AttachmentDto(saved.Id, saved.FileName, saved.SizeBytes));
    }

    /// <summary>
    /// ET-08: emails sent to a person, as candidate and as employee. HR see the history of people in their
    /// countries (the candidate's country or the employee's); others get 403.
    /// </summary>
    private static async Task<IResult> HistoryAsync(Guid personId, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        if (!await db.People.AnyAsync(p => p.Id == personId, ct))
        {
            return Results.NotFound();
        }

        var countries = await db.CandidateProfiles.Where(c => c.PersonId == personId).Select(c => c.CountryCode)
            .Concat(db.Employees.Where(e => e.PersonId == personId).Select(e => e.CountryCode))
            .ToListAsync(ct);
        if (!countries.Any(access.HrCountries.Includes))
        {
            return Results.Forbid();
        }

        var logs = await db.EmailLog.AsNoTracking().Where(e => e.PersonId == personId).OrderByDescending(e => e.SentAtUtc).ToListAsync(ct);
        return Results.Ok(logs.Select(Dto).ToList());
    }

    private static Actor ActorOf(ICurrentUser user, UserAccess access) => new(user.UserId, user.DisplayName, access.EmployeeId);

    internal static EmailLogDto Dto(EmailLogEntry e) =>
        new(e.Id, e.RecordType, e.RecordId, e.TemplateName, e.TemplateVersion, e.Category, e.To, e.Cc, e.Sender, e.FromAddress, e.Subject, e.BodyHtml,
            e.Attachments.Select(a => a.FileName).ToList(), e.BatchId, e.SentByName, e.SentAtUtc, e.Status, e.Error);
}
