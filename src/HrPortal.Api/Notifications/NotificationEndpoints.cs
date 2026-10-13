using Hangfire;
using HrPortal.Api.Auth;
using HrPortal.Domain.Notifications;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Notifications;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Notifications;

/// <summary>Queues notification emails as Hangfire jobs, so failures are retried and never block the request.</summary>
public sealed class HangfireNotificationEmailQueue(IBackgroundJobClient jobs) : INotificationEmailQueue
{
    public void Enqueue(Guid notificationId) =>
        jobs.Enqueue<NotificationEmailJob>(job => job.SendAsync(notificationId, CancellationToken.None));
}

/// <summary>The signed-in user's in-app notifications (SUP-05). Nobody can read or mark anyone else's.</summary>
public static class NotificationEndpoints
{
    public sealed record NotificationDto(Guid Id, string Title, string Body, string Link, DateTimeOffset CreatedAtUtc, bool IsRead);

    public sealed record NotificationList(int UnreadCount, IReadOnlyList<NotificationDto> Items);

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization(Policies.Authenticated);

        group.MapGet("/", async (HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct) =>
        {
            var me = (await accessContext.GetAsync(ct)).EmployeeId;
            if (me is null)
            {
                return new NotificationList(0, []);
            }

            var mine = db.Notifications.AsNoTracking().Where(n => n.RecipientEmployeeId == me);
            var items = await mine.OrderByDescending(n => n.CreatedAtUtc).Take(50)
                .Select(n => new NotificationDto(n.Id, n.Title, n.Body, n.Link, n.CreatedAtUtc, n.ReadAtUtc != null))
                .ToListAsync(ct);
            return new NotificationList(await mine.CountAsync(n => n.ReadAtUtc == null, ct), items);
        });

        group.MapPost("/{id:guid}/read", async (Guid id, HrPortalDbContext db, IAccessContext accessContext, TimeProvider clock, CancellationToken ct) =>
        {
            var me = (await accessContext.GetAsync(ct)).EmployeeId;
            var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.RecipientEmployeeId == me, ct);
            if (notification is null)
            {
                return Results.NotFound();
            }

            notification.MarkRead(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/read-all", async (HrPortalDbContext db, IAccessContext accessContext, TimeProvider clock, CancellationToken ct) =>
        {
            var me = (await accessContext.GetAsync(ct)).EmployeeId;
            foreach (var notification in await db.Notifications.Where(n => n.RecipientEmployeeId == me && n.ReadAtUtc == null).ToListAsync(ct))
            {
                notification.MarkRead(clock.GetUtcNow());
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }
}
