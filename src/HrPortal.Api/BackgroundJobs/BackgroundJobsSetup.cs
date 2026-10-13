using Hangfire;
using Hangfire.PostgreSql;
using HrPortal.Infrastructure;
using HrPortal.Infrastructure.Employees;
using HrPortal.Infrastructure.Recruiting;

namespace HrPortal.Api.BackgroundJobs;

/// <summary>Hangfire on PostgreSQL, hosted in the API process.</summary>
public static class BackgroundJobsSetup
{
    /// <summary>Set to false to host no Hangfire server (integration tests run jobs directly).</summary>
    public const string RunServerKey = "BackgroundJobs:RunServer";

    public static WebApplicationBuilder AddBackgroundJobs(this WebApplicationBuilder builder)
    {
        builder.Services.AddHangfire((sp, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                options => options.UseNpgsqlConnection(
                    sp.GetRequiredService<IConfiguration>().GetConnectionString(DependencyInjection.ConnectionStringName)),
                // Notification emails should go out within seconds, not the 15-second default.
                new PostgreSqlStorageOptions { QueuePollInterval = TimeSpan.FromSeconds(2) }));

        if (builder.Configuration.GetValue(RunServerKey, defaultValue: true))
        {
            builder.Services.AddHangfireServer();
        }

        return builder;
    }

    /// <summary>
    /// Applies scheduled job changes hourly: effective dates start at local midnight in each office's time zone,
    /// so a daily UTC run would apply Pakistan changes five hours late.
    /// </summary>
    public static void ScheduleRecurringJobs(this WebApplication app) =>
        app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<ScheduledChangeRunner>(
            ScheduledChangeRunner.RecurringJobId,
            runner => runner.ApplyDueAsync(CancellationToken.None),
            Cron.Hourly());

    /// <summary>
    /// One-time jobs, enqueued only while there is work for them, so they run once after the deployment that needs
    /// them; each is safe to run again. CV text extraction makes CVs uploaded before CV search existed searchable.
    /// </summary>
    public static async Task EnqueueOneTimeJobsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        if (await scope.ServiceProvider.GetRequiredService<CvTextBackfill>().IsNeededAsync(CancellationToken.None))
        {
            app.Services.GetRequiredService<IBackgroundJobClient>().Enqueue<CvTextBackfill>(job => job.RunAsync(CancellationToken.None));
        }
    }
}
