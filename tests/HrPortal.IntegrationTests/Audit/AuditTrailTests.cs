using HrPortal.Domain.Audit;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HrPortal.IntegrationTests.Audit;

[Collection(ApiCollection.Name)]
public class AuditTrailTests(HrPortalApiFactory factory)
{
    [Fact]
    public async Task Audit_rows_cannot_be_updated_through_EF()
    {
        _ = factory.CreateClient();
        await factory.WithDbAsync(async db =>
        {
            db.AuditEntries.Add(AuditEntry.Create(DateTimeOffset.UtcNow, "t", "t", AuditAction.Viewed, "Test", "1"));
            await db.SaveChangesAsync();

            var entry = await db.AuditEntries.OrderByDescending(a => a.Id).FirstAsync();
            db.Entry(entry).Property(e => e.ActorId).CurrentValue = "tampered";

            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            return 0;
        });
    }

    [Theory]
    [InlineData("""UPDATE "AuditEntries" SET "ActorId" = 'tampered'""")]
    [InlineData("""DELETE FROM "AuditEntries" """)]
    [InlineData("""TRUNCATE "AuditEntries" """)]
    public async Task Audit_rows_cannot_be_changed_with_raw_SQL(string sql)
    {
        _ = factory.CreateClient();
        await factory.WithDbAsync(async db =>
        {
            db.AuditEntries.Add(AuditEntry.Create(DateTimeOffset.UtcNow, "t", "t", AuditAction.Viewed, "Test", "2"));
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Contains("append-only", ex.MessageText);
            return 0;
        });
    }

    [Fact]
    public async Task Explicit_view_is_recorded()
    {
        _ = factory.CreateClient();
        await factory.WithDbAsync(async db =>
        {
            var log = new HrPortal.Infrastructure.Audit.AuditLog(db, new SystemUser(), TimeProvider.System);
            await log.RecordViewAsync("Compensation", "emp-123");

            Assert.True(await db.AuditEntries.AnyAsync(a =>
                a.Action == AuditAction.Viewed && a.EntityType == "Compensation" && a.EntityId == "emp-123"));
            return 0;
        });
    }

    private sealed class SystemUser : HrPortal.Domain.Common.ICurrentUser
    {
        public bool IsAuthenticated => false;

        public string UserId => "system";

        public string DisplayName => "System";

        public IReadOnlySet<string> Roles => new HashSet<string>();
    }
}
