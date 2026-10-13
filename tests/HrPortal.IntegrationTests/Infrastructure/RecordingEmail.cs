using System.Collections.Concurrent;
using HrPortal.Domain.Notifications;
using HrPortal.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace HrPortal.IntegrationTests.Infrastructure;

/// <summary>Captures sent email instead of talking to SMTP.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    /// <summary>Addresses the "mail server" rejects, to test failed sends.</summary>
    public ConcurrentDictionary<string, bool> Bounce { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (message.To.Any(Bounce.ContainsKey))
        {
            throw new System.Net.Mail.SmtpFailedRecipientException("Mailbox unavailable");
        }

        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string address) =>
        Sent.Where(m => m.To.Contains(address, StringComparer.OrdinalIgnoreCase)).ToList();
}

/// <summary>Runs the email job straight away (in its own scope, as Hangfire would) instead of queueing it.</summary>
public sealed class InlineNotificationEmailQueue(IServiceProvider services) : INotificationEmailQueue
{
    public void Enqueue(Guid notificationId)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<NotificationEmailJob>().SendAsync(notificationId, CancellationToken.None).GetAwaiter().GetResult();
    }
}

/// <summary>Captures calendar calls; tests never reach Microsoft 365.</summary>
public sealed class RecordingCalendarService : ICalendarService
{
    public ConcurrentQueue<(string EventId, CalendarEvent Event)> Created { get; } = new();

    public ConcurrentQueue<string> Cancelled { get; } = new();

    public Task<string> CreateEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        var id = $"test-{Guid.NewGuid():N}";
        Created.Enqueue((id, calendarEvent));
        return Task.FromResult(id);
    }

    public Task CancelEventAsync(string organizerEmail, string eventId, CancellationToken cancellationToken = default)
    {
        Cancelled.Enqueue(eventId);
        return Task.CompletedTask;
    }
}
