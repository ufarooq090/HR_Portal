using HrPortal.Domain.Audit;
using HrPortal.Domain.Common;
using HrPortal.Infrastructure.Persistence;

namespace HrPortal.Infrastructure.Audit;

public sealed class AuditLog(HrPortalDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditLog
{
    public async Task RecordViewAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(AuditEntry.Create(
            clock.GetUtcNow(), currentUser.UserId, currentUser.DisplayName, AuditAction.Viewed, entityType, entityId));
        await db.SaveChangesAsync(cancellationToken);
    }
}
