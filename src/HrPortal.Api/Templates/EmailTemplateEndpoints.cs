using HrPortal.Api.Auth;
using HrPortal.Domain.Common;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;
using HrPortal.Domain.Templates;
using HrPortal.Infrastructure.Persistence;
using HrPortal.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Templates;

/// <summary>
/// Email templates (ET-01, ET-02, ET-06). HR Admins create and edit them; every change is a new version. HR and
/// hiring managers list the templates they may use (hiring managers: interview templates only).
/// </summary>
public static class EmailTemplateEndpoints
{
    public sealed record TemplateSummary(
        Guid Id,
        string Name,
        TemplateCategory Category,
        TemplateRecordType RecordType,
        string? CountryCode,
        bool IsActive,
        bool IsRestricted,
        SenderMailbox DefaultSender,
        bool CcManager,
        string? Trigger,
        int Version,
        DateTimeOffset UpdatedAtUtc);

    public sealed record VersionSummary(int Version, string ChangedByName, DateTimeOffset AtUtc);

    public sealed record TemplateDetail(
        Guid Id,
        string Name,
        TemplateCategory Category,
        TemplateRecordType RecordType,
        string Subject,
        string BodyHtml,
        string? CountryCode,
        bool IsActive,
        bool IsRestricted,
        SenderMailbox DefaultSender,
        bool CcManager,
        string? Trigger,
        int Version,
        DateTimeOffset UpdatedAtUtc,
        IReadOnlyList<VersionSummary> Versions,
        bool CanManage);

    public sealed record VersionDetail(
        int Version, string Name, TemplateCategory Category, string Subject, string BodyHtml, string? CountryCode, SenderMailbox DefaultSender,
        bool CcManager, bool IsActive, string ChangedByName, DateTimeOffset AtUtc);

    public sealed record TemplateRequest(
        string Name,
        TemplateCategory Category,
        TemplateRecordType RecordType,
        string Subject,
        string BodyHtml,
        string? CountryCode,
        SenderMailbox DefaultSender,
        bool CcManager,
        bool IsActive);

    public static IEndpointRouteBuilder MapEmailTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/email-templates").WithTags("Email templates");
        var use = Policies.Module(HrModule.EmailTemplates, Capability.Use);

        group.MapGet("/", ListAsync).RequireAuthorization(use);
        group.MapGet("/merge-fields", (TemplateRecordType? recordType) =>
                Results.Ok(recordType is { } type ? MergeFields.For(type) : MergeFields.Catalog))
            .RequireAuthorization(use);
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(use);
        group.MapGet("/{id:guid}/versions/{version:int}", VersionAsync).RequireAuthorization(Policies.Module(HrModule.EmailTemplates, Capability.Edit));
        group.MapPost("/", CreateAsync).RequireAuthorization(Policies.Module(HrModule.EmailTemplates, Capability.Create));
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(Policies.Module(HrModule.EmailTemplates, Capability.Edit));
        return app;
    }

    private static async Task<IResult> ListAsync(
        TemplateRecordType? recordType, string? country, bool? activeOnly, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var query = db.EmailTemplates.AsNoTracking();
        if (recordType is { } type)
        {
            query = query.Where(t => t.RecordType == type);
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            var code = country.Trim().ToUpperInvariant();
            query = query.Where(t => t.CountryCode == null || t.CountryCode == code);
        }

        if (activeOnly == true)
        {
            query = query.Where(t => t.IsActive);
        }

        var templates = await query.OrderBy(t => t.Category).ThenBy(t => t.Name).ToListAsync(ct);
        return Results.Ok(templates.Where(t => TemplateRules.CanSee(access, t)).Select(Summary).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var template = await db.EmailTemplates.AsNoTracking().Include(t => t.Versions).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return Results.NotFound();
        }

        return TemplateRules.CanSee(access, template) ? Results.Ok(Detail(template, TemplateRules.CanManage(access))) : Results.Forbid();
    }

    private static async Task<IResult> VersionAsync(Guid id, int version, HrPortalDbContext db, CancellationToken ct)
    {
        var v = await db.EmailTemplateVersions.AsNoTracking().SingleOrDefaultAsync(v => v.TemplateId == id && v.Version == version, ct);
        return v is null
            ? Results.NotFound()
            : Results.Ok(new VersionDetail(
                v.Version, v.Name, v.Category, v.Subject, v.BodyHtml, v.CountryCode, v.DefaultSender, v.CcManager, v.IsActive, v.ChangedByName, v.AtUtc));
    }

    private static async Task<IResult> CreateAsync(
        TemplateRequest request, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, TimeProvider clock, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        if (Content(request) is not { } content || !CoversCountry(access, content.CountryCode))
        {
            return Results.Forbid();
        }

        await EnsureUniqueNameAsync(db, content.Name, null, ct);
        var template = EmailTemplate.Create(content, ActorOf(user, access), clock.GetUtcNow());
        db.EmailTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/email-templates/{template.Id}", Detail(template, canManage: true));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, TemplateRequest request, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, TimeProvider clock, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var template = await db.EmailTemplates.Include(t => t.Versions).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return Results.NotFound();
        }

        // Changing a template affects every country it is used in, so HR Admins edit only templates within their countries.
        if (Content(request) is not { } content || !CoversCountry(access, template.CountryCode) || !CoversCountry(access, content.CountryCode))
        {
            return Results.Forbid();
        }

        await EnsureUniqueNameAsync(db, content.Name, id, ct);
        template.Update(content, ActorOf(user, access), clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Results.Ok(Detail(template, canManage: true));
    }

    /// <summary>The request as template content, with the body sanitised (no scripts, styles or unsafe links).</summary>
    private static TemplateContent? Content(TemplateRequest r) =>
        new(r.Name, r.Category, r.RecordType, r.Subject, EmailHtml.Sanitize(r.BodyHtml ?? string.Empty),
            string.IsNullOrWhiteSpace(r.CountryCode) ? null : r.CountryCode.Trim().ToUpperInvariant(), r.DefaultSender, r.CcManager, r.IsActive);

    /// <summary>A template for both countries needs access to every country; a one-country template, that country.</summary>
    private static bool CoversCountry(UserAccess access, string? countryCode) =>
        countryCode is null ? access.HrCountries.IsAll : access.HrCountries.Includes(countryCode);

    private static async Task EnsureUniqueNameAsync(HrPortalDbContext db, string name, Guid? except, CancellationToken ct)
    {
        var trimmed = name.Trim();
        if (await db.EmailTemplates.AnyAsync(t => t.Name.ToLower() == trimmed.ToLower() && t.Id != except, ct))
        {
            throw new DomainValidationException("name", "Another template already has this name.");
        }
    }

    private static Actor ActorOf(ICurrentUser user, UserAccess access) => new(user.UserId, user.DisplayName, access.EmployeeId);

    private static TemplateSummary Summary(EmailTemplate t) =>
        new(t.Id, t.Name, t.Category, t.RecordType, t.CountryCode, t.IsActive, t.IsRestricted, t.DefaultSender, t.CcManager, t.Trigger, t.Version, t.UpdatedAtUtc);

    private static TemplateDetail Detail(EmailTemplate t, bool canManage) =>
        new(t.Id, t.Name, t.Category, t.RecordType, t.Subject, t.BodyHtml, t.CountryCode, t.IsActive, t.IsRestricted, t.DefaultSender, t.CcManager,
            t.Trigger, t.Version, t.UpdatedAtUtc,
            t.Versions.OrderByDescending(v => v.Version).Select(v => new VersionSummary(v.Version, v.ChangedByName, v.AtUtc)).ToList(),
            canManage);
}
