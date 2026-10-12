namespace HrPortal.Domain.Notifications;

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record EmailMessage(
    IReadOnlyList<string> To,
    string Subject,
    string HtmlBody,
    IReadOnlyList<string>? Cc = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>Sends email. Microsoft Graph in Azure; Mailpit locally; a fake in tests.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
