using System.Text.Json.Serialization;
using HrPortal.Domain.Common;
using HrPortal.Domain.Requisitions;

namespace HrPortal.Domain.Templates;

[JsonConverter(typeof(JsonStringEnumConverter<EmailStatus>))]
public enum EmailStatus
{
    Sent,
    Failed,
}

/// <summary>A file sent with an email, kept in file storage (container <see cref="EmailLogEntry.AttachmentContainer"/>).</summary>
public sealed record EmailLogAttachment(string FileName, string ContentType, long SizeBytes, string StorageKey);

/// <summary>
/// ET-08: one email sent (or attempted) from the portal, logged on the person it went to, with the record it was
/// sent from. Never changed after it is written.
/// </summary>
public sealed class EmailLogEntry : Entity
{
    public const string AttachmentContainer = "email-attachments";

    private EmailLogEntry()
    {
    }

    public EmailLogEntry(
        Guid personId,
        Guid? employeeId,
        TemplateRecordType recordType,
        Guid recordId,
        EmailTemplate? template,
        string to,
        IReadOnlyList<string> cc,
        SenderMailbox sender,
        string fromAddress,
        string subject,
        string bodyHtml,
        IReadOnlyList<EmailLogAttachment> attachments,
        Guid? batchId,
        Actor actor,
        DateTimeOffset at)
    {
        PersonId = personId;
        EmployeeId = employeeId;
        RecordType = recordType;
        RecordId = recordId;
        TemplateId = template?.Id;
        TemplateName = template?.Name;
        TemplateVersion = template?.Version;
        Category = template?.Category;
        To = to;
        Cc = [.. cc];
        Sender = sender;
        FromAddress = fromAddress;
        Subject = subject;
        BodyHtml = bodyHtml;
        Attachments = [.. attachments];
        BatchId = batchId;
        SentById = actor.UserId;
        SentByName = actor.Name;
        SentAtUtc = at.ToUniversalTime();
        Status = EmailStatus.Sent;
    }

    /// <summary>The recipient's person record; the email shows in their history as candidate and as employee.</summary>
    public Guid PersonId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public TemplateRecordType RecordType { get; private set; }

    /// <summary>The application, employee or requisition the email was sent from.</summary>
    public Guid RecordId { get; private set; }

    public Guid? TemplateId { get; private set; }

    public string? TemplateName { get; private set; }

    public int? TemplateVersion { get; private set; }

    public TemplateCategory? Category { get; private set; }

    public string To { get; private set; } = null!;

    public List<string> Cc { get; private set; } = [];

    public SenderMailbox Sender { get; private set; }

    public string FromAddress { get; private set; } = null!;

    public string Subject { get; private set; } = null!;

    /// <summary>Kept here once; not copied into the audit trail.</summary>
    [NotAudited]
    public string BodyHtml { get; private set; } = null!;

    public List<EmailLogAttachment> Attachments { get; private set; } = [];

    /// <summary>Set for bulk sends, so one send's emails can be listed together.</summary>
    public Guid? BatchId { get; private set; }

    public string SentById { get; private set; } = null!;

    public string SentByName { get; private set; } = null!;

    public DateTimeOffset SentAtUtc { get; private set; }

    public EmailStatus Status { get; private set; }

    /// <summary>Why sending failed (never the email's content).</summary>
    public string? Error { get; private set; }

    public void MarkFailed(string error)
    {
        Status = EmailStatus.Failed;
        Error = Guard.RequiredText("error", error.Length > 500 ? error[..500] : error, 500);
    }
}

/// <summary>
/// A file uploaded to attach to an email (ET-01), before it is sent. Only the person who uploaded it can attach it;
/// once sent it is listed on the email log entry.
/// </summary>
public sealed class EmailAttachmentUpload : Entity
{
    public const int MaxPerEmail = 5;

    private EmailAttachmentUpload()
    {
    }

    public EmailAttachmentUpload(string storageKey, string fileName, string contentType, long sizeBytes, string uploadedById, DateTimeOffset at)
    {
        StorageKey = storageKey;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedById = uploadedById;
        UploadedAtUtc = at.ToUniversalTime();
    }

    public string StorageKey { get; private set; } = null!;

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public string UploadedById { get; private set; } = null!;

    public DateTimeOffset UploadedAtUtc { get; private set; }

    public EmailLogAttachment ToLog() => new(FileName, ContentType, SizeBytes, StorageKey);
}
