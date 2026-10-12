using HrPortal.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace HrPortal.Infrastructure.Notifications;

/// <summary>Local stand-in for Outlook calendar: logs the call instead of contacting Microsoft 365.</summary>
public sealed partial class LoggingCalendarService(ILogger<LoggingCalendarService> logger) : ICalendarService
{
    public Task<string> CreateEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        var eventId = $"local-{Guid.NewGuid():N}";
        LogCreated(eventId, calendarEvent.Subject, calendarEvent.StartUtc, calendarEvent.EndUtc, calendarEvent.Attendees.Count);
        return Task.FromResult(eventId);
    }

    public Task CancelEventAsync(string organizerEmail, string eventId, CancellationToken cancellationToken = default)
    {
        LogCancelled(eventId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Calendar event {EventId} created: '{Subject}' {Start:o}–{End:o} with {AttendeeCount} attendee(s)")]
    private partial void LogCreated(string eventId, string subject, DateTimeOffset start, DateTimeOffset end, int attendeeCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Calendar event {EventId} cancelled")]
    private partial void LogCancelled(string eventId);
}
