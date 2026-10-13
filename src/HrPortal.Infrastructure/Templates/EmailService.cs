using HrPortal.Domain.Common;
using HrPortal.Domain.Files;
using HrPortal.Domain.Notifications;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;
using HrPortal.Domain.Templates;
using HrPortal.Infrastructure.Notifications;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HrPortal.Infrastructure.Templates;

/// <summary>The record or template does not exist (404).</summary>
public sealed class EmailNotFoundException(string message) : Exception(message);

/// <summary>The user may not send this template for this record (403).</summary>
public sealed class EmailDeniedException(string message) : Exception(message);

/// <param name="Missing">Merge fields without a value (key and label); they block sending (ET-02).</param>
/// <param name="Problems">Other reasons the email cannot be sent, e.g. no email address.</param>
public sealed record ComposedEmail(
    EmailTemplate Template,
    EmailRecord Record,
    string? To,
    IReadOnlyList<string> Cc,
    string Subject,
    string BodyHtml,
    IReadOnlyList<MergeField> Missing,
    IReadOnlyList<string> Problems)
{
    public bool CanSend => Missing.Count == 0 && Problems.Count == 0;

    /// <summary>One sentence for HR: why this email cannot go.</summary>
    public string Blocker => string.Join(" ", Problems.Concat(
        Missing.Count == 0 ? [] : [$"These merge fields have no value: {string.Join(", ", Missing.Select(m => m.Label))}."]));
}

public sealed record BulkItem(Guid RecordId, string Name, string? To, Guid? EmailId, string? Error);

public sealed record BulkResult(Guid BatchId, IReadOnlyList<BulkItem> Sent, IReadOnlyList<BulkItem> Failed);

/// <summary>
/// ET-02..05, ET-08: fills a template from a record, checks the user may send it, sends it through
/// <see cref="IEmailSender"/> and logs it on the recipient's record. Every check happens here, on the server,
/// whatever the UI allowed.
/// </summary>
public sealed class EmailService(
    HrPortalDbContext db,
    IEmailSender sender,
    IFileStore files,
    IOptions<PortalOptions> portal,
    TimeProvider clock,
    ILogger<EmailService> logger)
{
    public const int MaxBulkRecipients = 200;

    public async Task<EmailTemplate> TemplateAsync(Guid templateId, CancellationToken ct) =>
        await db.EmailTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == templateId, ct)
        ?? throw new EmailNotFoundException("The template does not exist.");

    /// <summary>
    /// Fills the template (or HR's edited subject and body) for the record. Throws when the record does not exist
    /// or the user may not send this template for it.
    /// </summary>
    /// <param name="subject">HR's edited subject; null for the template's.</param>
    /// <param name="bodyHtml">HR's edited body; null for the template's. Sanitised here.</param>
    public async Task<ComposedEmail> ComposeAsync(
        UserAccess access, Actor actor, EmailTemplate template, Guid recordId, bool bulk, string? subject, string? bodyHtml, CancellationToken ct)
    {
        var record = await EmailRecords.LoadAsync(db, template.RecordType, recordId, actor.Name, portal.Value, clock.GetUtcNow(), ct)
            ?? throw new EmailNotFoundException("The record does not exist.");
        var facts = new SendFacts(
            record.CountryCode,
            IsHr: access.HrCountries.Includes(record.CountryCode),
            IsHiringManager: record.Type == TemplateRecordType.Application
                && RecruitingRules.IsHiringManagerFor(access, record.RequesterEmployeeId!.Value, record.HiringManagerEmployeeId!.Value));
        if (TemplateRules.WhyNotSend(access, template, facts, bulk) is { } reason)
        {
            throw new EmailDeniedException(reason);
        }

        var problems = new List<string>();
        if (!template.IsActive)
        {
            problems.Add("The template is inactive.");
        }

        if (!template.AppliesTo(record.CountryCode))
        {
            problems.Add($"The template is for {template.CountryCode} only, and this record is in {record.CountryCode}.");
        }

        if (string.IsNullOrWhiteSpace(record.RecipientEmail))
        {
            problems.Add(record.Type == TemplateRecordType.Requisition
                ? "The requisition has no approval step waiting, so there is no one to send this to."
                : $"{record.RecipientName} has no email address.");
        }

        var cc = new List<string>();
        if (template.CcManager)
        {
            if (record.ManagerEmail is { } manager)
            {
                cc.Add(manager);
            }
            else
            {
                problems.Add($"{record.RecipientName} has no manager to copy.");
            }
        }

        var body = bodyHtml is null ? template.BodyHtml : EmailHtml.Sanitize(bodyHtml);
        if (EmailHtml.Text(body).Length == 0)
        {
            problems.Add("The email is empty.");
        }

        var filledSubject = MergeFields.Fill((subject ?? template.Subject).Trim(), record.Values, html: false);
        var filledBody = MergeFields.Fill(body, record.Values, html: true);
        var missing = filledSubject.Missing.Concat(filledBody.Missing).Distinct()
            .Select(key => MergeFields.Catalog.FirstOrDefault(f => f.Key == key) ?? new MergeField(key, $"{{{{{key}}}}} (unknown field)", string.Empty, []))
            .ToList();
        if (filledSubject.Text.Length is 0 or > EmailTemplate.MaxSubjectLength)
        {
            problems.Add($"The subject must be 1 to {EmailTemplate.MaxSubjectLength} characters.");
        }

        return new ComposedEmail(template, record, record.RecipientEmail, cc, filledSubject.Text, filledBody.Text, missing, problems);
    }

    /// <summary>ET-03/04: sends one email, as filled (and possibly edited) by HR, and logs it (ET-08).</summary>
    public async Task<EmailLogEntry> SendAsync(
        UserAccess access, Actor actor, Guid templateId, Guid recordId, string? subject, string? bodyHtml, SenderMailbox mailbox,
        IReadOnlyList<Guid> attachmentIds, CancellationToken ct)
    {
        var template = await TemplateAsync(templateId, ct);
        var email = await ComposeAsync(access, actor, template, recordId, bulk: false, subject, bodyHtml, ct);
        if (!email.CanSend)
        {
            throw new DomainValidationException("bodyHtml", email.Blocker);
        }

        var (from, fromAddress) = await SenderAsync(mailbox, actor, ct);
        var attachments = await AttachmentsAsync(attachmentIds, actor, ct);
        return await DeliverAsync(email, mailbox, from, fromAddress, attachments, batchId: null, actor, ct);
    }

    /// <summary>ET-03: sends the filled email to the user's own work address. Not logged on the record.</summary>
    public async Task<string> SendTestAsync(
        UserAccess access, Actor actor, Guid templateId, Guid recordId, string? subject, string? bodyHtml, SenderMailbox mailbox,
        IReadOnlyList<Guid> attachmentIds, CancellationToken ct)
    {
        var me = await MyMailboxAsync(actor, ct) ?? throw new DomainValidationException("to", "You have no work email address to send a test to.");
        var template = await TemplateAsync(templateId, ct);
        var email = await ComposeAsync(access, actor, template, recordId, bulk: false, subject, bodyHtml, ct);
        var (from, _) = await SenderAsync(mailbox, actor, ct);
        var attachments = await AttachmentsAsync(attachmentIds, actor, ct);
        var note = email.CanSend ? string.Empty : $"<p><strong>Test only. This email could not be sent yet: {System.Net.WebUtility.HtmlEncode(email.Blocker)}</strong></p><hr>";
        await sender.SendAsync(
            new EmailMessage([me], $"[Test] {email.Subject}", note + email.BodyHtml, null, await ContentAsync(attachments, ct), from), ct);
        return me;
    }

    /// <summary>
    /// ET-05: sends the template to many records, each personalised. Records that cannot be sent (missing fields, no
    /// address, out of scope) are reported, not sent; the rest go out and are logged with a shared batch id.
    /// </summary>
    public async Task<BulkResult> SendBulkAsync(
        UserAccess access, Actor actor, Guid templateId, IReadOnlyList<Guid> recordIds, SenderMailbox mailbox, IReadOnlyList<Guid> attachmentIds, CancellationToken ct)
    {
        var template = await TemplateAsync(templateId, ct);
        if (template.IsRestricted)
        {
            throw new EmailDeniedException("Termination communication is sent to one employee at a time, never in bulk.");
        }

        var ids = recordIds.Distinct().ToList();
        if (ids.Count is 0 or > MaxBulkRecipients)
        {
            throw new DomainValidationException("recordIds", $"Choose 1 to {MaxBulkRecipients} recipients.");
        }

        var (from, fromAddress) = await SenderAsync(mailbox, actor, ct);
        var attachments = await AttachmentsAsync(attachmentIds, actor, ct);
        var batchId = Guid.CreateVersion7();
        var sent = new List<BulkItem>();
        var failed = new List<BulkItem>();
        foreach (var id in ids)
        {
            ComposedEmail email;
            try
            {
                email = await ComposeAsync(access, actor, template, id, bulk: true, null, null, ct);
            }
            catch (Exception e) when (e is EmailNotFoundException or EmailDeniedException)
            {
                failed.Add(new BulkItem(id, "(unknown)", null, null, e.Message));
                continue;
            }

            if (!email.CanSend)
            {
                failed.Add(new BulkItem(id, email.Record.RecipientName, email.To, null, email.Blocker));
                continue;
            }

            var log = await DeliverAsync(email, mailbox, from, fromAddress, attachments, batchId, actor, ct);
            (log.Status == EmailStatus.Sent ? sent : failed).Add(new BulkItem(id, email.Record.RecipientName, email.To, log.Id, log.Error));
        }

        return new BulkResult(batchId, sent, failed);
    }

    /// <summary>Stores an attachment for a later send.</summary>
    public async Task<EmailAttachmentUpload> UploadAsync(UploadedFile file, Stream content, Actor actor, CancellationToken ct)
    {
        var key = await files.SaveAsync(EmailLogEntry.AttachmentContainer, file.FileName, file.ContentType, content, ct);
        var upload = new EmailAttachmentUpload(key, file.FileName, file.ContentType, file.SizeBytes, actor.UserId, clock.GetUtcNow());
        db.EmailAttachmentUploads.Add(upload);
        await db.SaveChangesAsync(ct);
        return upload;
    }

    private async Task<EmailLogEntry> DeliverAsync(
        ComposedEmail email, SenderMailbox mailbox, EmailAddress? from, string fromAddress, IReadOnlyList<EmailAttachmentUpload> attachments, Guid? batchId,
        Actor actor, CancellationToken ct)
    {
        var record = email.Record;
        var log = new EmailLogEntry(
            record.PersonId, record.EmployeeId, record.Type, record.RecordId, email.Template, email.To!, email.Cc, mailbox, fromAddress,
            email.Subject, email.BodyHtml, attachments.Select(a => a.ToLog()).ToList(), batchId, actor, clock.GetUtcNow());
        try
        {
            await sender.SendAsync(new EmailMessage([email.To!], email.Subject, email.BodyHtml, email.Cc, await ContentAsync(attachments, ct), from), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The address and content are not logged; the log entry shows HR what failed.
            logger.LogWarning(e, "Email {EmailId} could not be sent", log.Id);
            log.MarkFailed($"The mail server did not accept the email ({e.GetType().Name}).");
        }

        db.EmailLog.Add(log);
        await db.SaveChangesAsync(ct);
        return log;
    }

    /// <summary>OL-02: the HR mailbox or the user's own. Phase 1 sends through IEmailSender with this as the From address.</summary>
    private async Task<(EmailAddress? From, string Address)> SenderAsync(SenderMailbox mailbox, Actor actor, CancellationToken ct)
    {
        if (mailbox == SenderMailbox.CurrentUser)
        {
            var me = await MyMailboxAsync(actor, ct) ?? throw new DomainValidationException("sender", "You have no work mailbox; send from the HR mailbox instead.");
            return (new EmailAddress(me, actor.Name), me);
        }

        var hr = portal.Value.HrMailboxAddress;
        return string.IsNullOrWhiteSpace(hr) ? (null, "(default sender)") : (new EmailAddress(hr, portal.Value.HrMailboxName), hr);
    }

    private Task<string?> MyMailboxAsync(Actor actor, CancellationToken ct) =>
        actor.EmployeeId is { } id ? db.Employees.Where(e => e.Id == id).Select(e => (string?)e.WorkEmail).SingleOrDefaultAsync(ct) : Task.FromResult<string?>(null);

    private async Task<IReadOnlyList<EmailAttachmentUpload>> AttachmentsAsync(IReadOnlyList<Guid> ids, Actor actor, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count > EmailAttachmentUpload.MaxPerEmail)
        {
            throw new DomainValidationException("attachmentIds", $"Attach at most {EmailAttachmentUpload.MaxPerEmail} files.");
        }

        var uploads = await db.EmailAttachmentUploads.AsNoTracking().Where(u => distinct.Contains(u.Id) && u.UploadedById == actor.UserId).ToListAsync(ct);
        if (uploads.Count != distinct.Count)
        {
            throw new DomainValidationException("attachmentIds", "One of the attachments was not found; upload it again.");
        }

        if (uploads.Sum(u => u.SizeBytes) > 20 * 1024 * 1024)
        {
            throw new DomainValidationException("attachmentIds", "Attachments can be at most 20 MB in total.");
        }

        return uploads;
    }

    private async Task<IReadOnlyList<EmailAttachment>?> ContentAsync(IReadOnlyList<EmailAttachmentUpload> uploads, CancellationToken ct)
    {
        if (uploads.Count == 0)
        {
            return null;
        }

        var result = new List<EmailAttachment>();
        foreach (var upload in uploads)
        {
            await using var stream = await files.OpenReadAsync(EmailLogEntry.AttachmentContainer, upload.StorageKey, ct);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, ct);
            result.Add(new EmailAttachment(upload.FileName, upload.ContentType, copy.ToArray()));
        }

        return result;
    }
}
