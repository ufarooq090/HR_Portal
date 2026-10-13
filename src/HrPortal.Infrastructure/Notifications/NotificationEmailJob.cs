using System.Net;
using HrPortal.Domain.Notifications;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HrPortal.Infrastructure.Notifications;

public sealed class PortalOptions
{
    public const string SectionName = "Portal";

    /// <summary>Public address of the web app, used for links in emails, e.g. https://hr.example.com.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Used for {{company_name}} where no legal entity applies (candidates, requisitions).</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>The shared HR mailbox emails are sent from (OL-02), e.g. hr@company.com.</summary>
    public string HrMailboxAddress { get; set; } = string.Empty;

    public string HrMailboxName { get; set; } = "HR";
}

/// <summary>
/// Emails a saved in-app notification to the recipient's work address via <see cref="IEmailSender"/>. Run in the
/// background (Hangfire retries failures), so a mail outage never blocks an approval. Sends at most once.
/// </summary>
public sealed class NotificationEmailJob(HrPortalDbContext db, IEmailSender email, IOptions<PortalOptions> portal, TimeProvider clock)
{
    public async Task SendAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == notificationId, cancellationToken);
        if (notification is null || notification.EmailedAtUtc is not null)
        {
            return;
        }

        var to = await db.Employees.Where(e => e.Id == notification.RecipientEmployeeId).Select(e => e.WorkEmail).SingleAsync(cancellationToken);
        await email.SendAsync(new EmailMessage([to], notification.Title, Html(notification)), cancellationToken);

        notification.MarkEmailed(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>User-entered text (titles, comments) is HTML-encoded.</summary>
    private string Html(Notification n)
    {
        var link = WebUtility.HtmlEncode(portal.Value.BaseUrl.TrimEnd('/') + n.Link);
        return $"""
            <p>{WebUtility.HtmlEncode(n.Body).Replace("\n", "<br>", StringComparison.Ordinal)}</p>
            <p><a href="{link}">Open in HR Portal</a> to review and act.</p>
            <p style="color:#666;font-size:12px">You received this because of your role in the HR Portal.</p>
            """;
    }
}
