using System.Text.Json.Serialization;
using HrPortal.Api.Auth;
using HrPortal.Api.Auth.DevAuth;
using HrPortal.Api.Errors;
using HrPortal.Api.Organisation;
using HrPortal.Infrastructure;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddHrPortalAuth();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainValidationExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<HrPortalDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous().WithTags("Health");
if (app.Configuration.GetValue<bool>(DevAuthGuard.ConfigKey))
{
    app.MapDevAuthEndpoints();
}

app.MapMeEndpoints();
app.MapCountrySettingsEndpoints();

await app.RunAsync();

public partial class Program;
