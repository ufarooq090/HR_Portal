using System.Security.Cryptography;
using HrPortal.Api.Auth.DevAuth;
using HrPortal.Domain.Files;
using HrPortal.Domain.Notifications;
using Microsoft.AspNetCore.TestHost;
using HrPortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HrPortal.IntegrationTests.Infrastructure;

/// <summary>Runs the API in the Test environment against a throwaway PostgreSQL container, signed in via DevAuth.</summary>
public sealed class HrPortalApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(DevAuthGuard.TestEnvironmentName);
        builder.UseSetting("ConnectionStrings:HrPortal", _postgres.GetConnectionString());
        builder.UseSetting(DevAuthGuard.ConfigKey, "true");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("BackgroundJobs:RunServer", "false");
        builder.UseSetting("FieldEncryption:ActiveKeyId", "test");
        builder.UseSetting("FieldEncryption:Keys:test", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Portal:BaseUrl", "https://hr.test");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IFileStore>(Files);
            services.AddSingleton<IEmailSender>(Emails);
            services.AddSingleton<INotificationEmailQueue, InlineNotificationEmailQueue>();
        });
    }

    public InMemoryFileStore Files { get; } = new();

    public RecordingEmailSender Emails { get; } = new();

    /// <summary>HTTP client signed in as a seeded dev user, e.g. <c>dev-pk-hradmin</c>.</summary>
    public HttpClient CreateClientAs(string devUserId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthenticationHandler.HeaderName, devUserId);
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<HrPortalDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<HrPortalDbContext>());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<HrPortalApiFactory>
{
    public const string Name = "Api";
}
