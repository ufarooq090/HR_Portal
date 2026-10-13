using System.Collections.Concurrent;
using HrPortal.Domain.Notifications;
using HrPortal.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace HrPortal.IntegrationTests.Infrastructure;

/// <summary>Captures sent email instead of talking to SMTP.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
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
