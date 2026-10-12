using System.Reflection;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Common;
using HrPortal.Domain.Organisation;
using HrPortal.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Infrastructure.Persistence;

public sealed class HrPortalDbContext(DbContextOptions<HrPortalDbContext> options, IFieldEncryptor encryptor)
    : DbContext(options)
{
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<CountrySettings> CountrySettings => Set<CountrySettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HrPortalDbContext).Assembly);
        ApplySensitiveFieldEncryption(modelBuilder);
    }

    /// <summary>Every property marked [Sensitive] is encrypted at rest.</summary>
    /// <remarks>The model is cached per process, so the encryptor (a singleton) is captured once.</remarks>
    private void ApplySensitiveFieldEncryption(ModelBuilder modelBuilder)
    {
        var converter = new EncryptedStringConverter(encryptor);
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            if (property.PropertyInfo?.GetCustomAttribute<SensitiveAttribute>() is null)
            {
                continue;
            }

            if (property.ClrType != typeof(string))
            {
                throw new NotSupportedException(
                    $"[Sensitive] is only supported on string properties ({property.DeclaringType.Name}.{property.Name}).");
            }

            property.SetValueConverter(converter);
        }
    }
}
