namespace HrPortal.Domain.Notifications;

public sealed record CalendarEvent(
    string OrganizerEmail,
    IReadOnlyList<string> Attendees,
    string Subject,
    string HtmlBody,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsOnlineMeeting);

/// <summary>Creates Outlook calendar events. Microsoft Graph in Azure; logged locally.</summary>
public interface ICalendarService
{
    /// <returns>The provider's event id, used for later updates or cancellation.</returns>
    Task<string> CreateEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default);

    Task CancelEventAsync(string organizerEmail, string eventId, CancellationToken cancellationToken = default);
}
