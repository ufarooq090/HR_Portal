using System.Reflection;
using System.Text.Json;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HrPortal.Infrastructure.Audit;

/// <summary>
/// Writes an <see cref="AuditEntry"/> for every create, update and delete in the same transaction,
/// and refuses any attempt to modify or delete existing audit rows.
/// </summary>
/// <remarks>Audited entities must use client-generated keys so the key is known before saving.</remarks>
public sealed class AuditSaveChangesInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    public const string MaskedValue = "***";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditEntries(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddAuditEntries(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void AddAuditEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();
        var audits = new List<AuditEntry>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditEntry)
            {
                if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    throw new InvalidOperationException("Audit entries are append-only and cannot be modified or deleted.");
                }

                continue;
            }

            AuditAction? action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Modified => AuditAction.Updated,
                EntityState.Deleted => AuditAction.Deleted,
                _ => null,
            };

            if (action is null)
            {
                continue;
            }

            var (oldValues, newValues) = CaptureValues(entry, action.Value);
            if (action == AuditAction.Updated && newValues.Count == 0)
            {
                continue;
            }

            audits.Add(AuditEntry.Create(
                now,
                currentUser.UserId,
                currentUser.DisplayName,
                action.Value,
                entry.Metadata.ClrType.Name,
                KeyOf(entry),
                oldValues.Count > 0 ? JsonSerializer.Serialize(oldValues) : null,
                newValues.Count > 0 ? JsonSerializer.Serialize(newValues) : null));
        }

        context.AddRange(audits);
    }

    private static (Dictionary<string, object?> Old, Dictionary<string, object?> New) CaptureValues(EntityEntry entry, AuditAction action)
    {
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            var sensitive = property.Metadata.PropertyInfo?.GetCustomAttribute<SensitiveAttribute>() is not null;
            object? Mask(object? value) => sensitive && value is not null ? MaskedValue : value;

            switch (action)
            {
                case AuditAction.Created:
                    newValues[name] = Mask(property.CurrentValue);
                    break;
                case AuditAction.Deleted:
                    oldValues[name] = Mask(property.OriginalValue);
                    break;
                case AuditAction.Updated when property.IsModified && !Equals(property.OriginalValue, property.CurrentValue):
                    oldValues[name] = Mask(property.OriginalValue);
                    newValues[name] = Mask(property.CurrentValue);
                    break;
            }
        }

        return (oldValues, newValues);
    }

    private static string KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is null
            ? string.Empty
            : string.Join(",", key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString()));
    }
}
