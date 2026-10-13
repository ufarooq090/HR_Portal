using HrPortal.Domain.Common;

namespace HrPortal.Domain.Notifications;

/// <summary>
/// An in-app notification (SUP-05), also emailed to the recipient's work address by a background job.
/// Per-user preferences come later; for now every notification is also emailed.
/// </summary>
public sealed class Notification : Entity
{
    private Notification()
    {
    }

    public Notification(Guid recipientEmployeeId, string title, string body, string link, DateTimeOffset createdAt)
    {
        RecipientEmployeeId = recipientEmployeeId;
        Title = Guard.RequiredText(nameof(title), title, 200);
        Body = Guard.RequiredText(nameof(body), body, 2000);
        Link = Guard.RequiredText(nameof(link), link, 300);
        CreatedAtUtc = createdAt.ToUniversalTime();
    }

    public Guid RecipientEmployeeId { get; private set; }

    public string Title { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    /// <summary>App-relative link, e.g. /requisitions/{id}.</summary>
    public string Link { get; private set; } = null!;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ReadAtUtc { get; private set; }

    public DateTimeOffset? EmailedAtUtc { get; private set; }

    public void MarkRead(DateTimeOffset at) => ReadAtUtc ??= at.ToUniversalTime();

    public void MarkEmailed(DateTimeOffset at) => EmailedAtUtc = at.ToUniversalTime();
}

/// <summary>Sends a saved notification's email in the background (Hangfire), with retries.</summary>
public interface INotificationEmailQueue
{
    void Enqueue(Guid notificationId);
}
