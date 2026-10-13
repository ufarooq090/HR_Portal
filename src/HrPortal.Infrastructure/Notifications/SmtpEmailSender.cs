using System.Net.Mail;
using System.Net.Mime;
using HrPortal.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace HrPortal.Infrastructure.Notifications;

public sealed class SmtpEmailOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public string FromAddress { get; set; } = "hr-portal@localhost";

    public string FromName { get; set; } = "HR Portal";
}

/// <summary>Plain SMTP sender used for local development (Mailpit). Deployed environments use Microsoft Graph.</summary>
public sealed class SmtpEmailSender(IOptions<SmtpEmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        using var mail = new MailMessage
        {
            From = message.From is { } from ? new MailAddress(from.Address, from.Name) : new MailAddress(settings.FromAddress, settings.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true,
        };

        foreach (var to in message.To)
        {
            mail.To.Add(to);
        }

        foreach (var cc in message.Cc ?? [])
        {
            mail.CC.Add(cc);
        }

        foreach (var attachment in message.Attachments ?? [])
        {
            var part = new Attachment(new MemoryStream(attachment.Content), attachment.FileName, attachment.ContentType);
            part.ContentDisposition!.DispositionType = DispositionTypeNames.Attachment;
            mail.Attachments.Add(part);
        }

        using var client = new SmtpClient(settings.Host, settings.Port);
        await client.SendMailAsync(mail, cancellationToken);
    }
}
