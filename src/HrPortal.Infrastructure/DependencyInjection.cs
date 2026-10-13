using Azure.Storage.Blobs;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Files;
using HrPortal.Domain.Recruiting;
using HrPortal.Infrastructure.Employees;
using HrPortal.Infrastructure.Recruiting;
using HrPortal.Infrastructure.Templates;
using HrPortal.Infrastructure.Files;
using HrPortal.Domain.Notifications;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Audit;
using HrPortal.Infrastructure.Notifications;
using HrPortal.Infrastructure.Persistence;
using HrPortal.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HrPortal.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "HrPortal";

    /// <summary>Registers persistence, audit, encryption and integrations. Requires an <c>ICurrentUser</c> registration.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<FieldEncryptionOptions>().Bind(configuration.GetSection(FieldEncryptionOptions.SectionName));
        services.AddSingleton<IFieldEncryptor, AesGcmFieldEncryptor>();

        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<HrPortalDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
            options.UseNpgsql(connectionString)
                .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAccessContext, AccessContext>();

        services.AddSingleton(sp => new BlobServiceClient(
            sp.GetRequiredService<IConfiguration>()[BlobFileStore.ConnectionStringKey]
            ?? throw new InvalidOperationException($"'{BlobFileStore.ConnectionStringKey}' is not configured.")));
        services.AddSingleton<IFileStore, BlobFileStore>();
        services.AddScoped<EmployeeImporter>();
        services.AddScoped<ScheduledChangeRunner>();
        services.AddOptions<PortalOptions>().Bind(configuration.GetSection(PortalOptions.SectionName));
        services.AddScoped<NotificationEmailJob>();
        services.AddSingleton<ICvTextExtractor, CvTextExtractor>();
        services.AddScoped<CvTextBackfill>();
        services.AddScoped<EmailService>();
        services.AddScoped<CandidateImporter>();

        // Microsoft Graph implementations replace these for deployed environments.
        services.AddOptions<SmtpEmailOptions>().Bind(configuration.GetSection("Email:Smtp"));
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<ICalendarService, LoggingCalendarService>();

        return services;
    }
}
