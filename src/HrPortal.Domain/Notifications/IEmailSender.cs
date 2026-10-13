namespace HrPortal.Domain.Notifications;

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>A sender address with its display name.</summary>
public sealed record EmailAddress(string Address, string? Name);

/// <param name="From">Null: the system's default sender.</param>
public sealed record EmailMessage(
    IReadOnlyList<string> To,
    string Subject,
    string HtmlBody,
    IReadOnlyList<string>? Cc = null,
    IReadOnlyList<EmailAttachment>? Attachments = null,
    EmailAddress? From = null);

/// <summary>Sends email. Microsoft Graph in Azure; Mailpit locally; a fake in tests.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
