using HrPortal.Api.Auth;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Files;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using HrPortal.Infrastructure.Recruiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Recruiting;

/// <summary>
/// Candidates (CT-02, CT-09): one <see cref="Person"/> per human, with a candidate profile and CVs, seen through the
/// applications the user may see. Salary figures only reach HR and the hiring manager, and each view is audited.
/// </summary>
public static class CandidateEndpoints
{
    public const string CompensationAuditType = "CandidateCompensation";

    /// <summary>Audit entity type for CV downloads; the entity id is the CV's id.</summary>
    public const string CvDownloadAuditType = "CandidateCvDownload";

    /// <summary>A row on the board or in the list view (CT-01).</summary>
    public sealed record ApplicationCard(
        Guid Id,
        Guid PersonId,
        string CandidateName,
        string? Email,
        string? CurrentEmployer,
        PipelineStage Stage,
        int? InterviewRound,
        CandidateSource Source,
        DateTimeOffset AppliedAtUtc,
        DateTimeOffset StageChangedAtUtc,
        Guid RequisitionId,
        string RequisitionNumber,
        string RequisitionTitle,
        string CountryCode,
        int InterviewCount);

    public sealed record MoneyDto(decimal Amount, string Currency);

    public sealed record DuplicateMatch(Guid PersonId, string Name, string? Email, string? Phone, bool MatchedOnEmail, bool MatchedOnPhone, bool IsEmployee);

    public sealed record DuplicateCheckRequest(string? Email, string? Phone);

    public sealed record CvDto(Guid Id, string FileName, long SizeBytes, string UploadedBy, DateTimeOffset UploadedAtUtc);

    public sealed record CandidateDetail(
        Guid PersonId,
        string GivenName,
        string FamilyName,
        string? Email,
        string? Phone,
        string? CurrentEmployer,
        int? NoticePeriodDays,
        MoneyDto? CurrentCtc,
        MoneyDto? ExpectedCtc,
        bool CanSeeCompensation,
        bool CanEdit,
        IReadOnlyList<ApplicationCard> Applications,
        IReadOnlyList<CvDto> Cvs);

    /// <summary>Candidate details, used both when adding a new candidate and when editing one.</summary>
    public sealed record CandidateRequest(
        string GivenName,
        string FamilyName,
        string? Email,
        string? Phone,
        string? CurrentEmployer,
        int? NoticePeriodDays,
        MoneyDto? CurrentCtc,
        MoneyDto? ExpectedCtc);

    public static IEndpointRouteBuilder MapCandidateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/candidates").WithTags("Recruiting");

        // Panelists without a recruiting role still reach the candidates they interview, so the records decide.
        group.MapGet("/", ListAsync).RequireAuthorization(Policies.Authenticated);
        group.MapPost("/duplicates", DuplicatesAsync).RequireAuthorization(Policies.Module(HrModule.Candidates, Capability.Create));
        group.MapGet("/{personId:guid}", GetAsync).RequireAuthorization(Policies.Authenticated);
        group.MapPut("/{personId:guid}", UpdateAsync).RequireAuthorization(Policies.Module(HrModule.Candidates, Capability.Edit));
        group.MapPost("/{personId:guid}/cvs", UploadCvAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadPolicy.MaxBytes + 64 * 1024))
            .RequireAuthorization(Policies.Module(HrModule.Candidates, Capability.Edit));
        group.MapGet("/{personId:guid}/cvs/{cvId:guid}/content", DownloadCvAsync).RequireAuthorization(Policies.Authenticated);
        return app;
    }

    /// <summary>CT-01 list view: every application the user may see, filtered.</summary>
    private static async Task<List<ApplicationCard>> ListAsync(
        string? search,
        Guid? requisitionId,
        PipelineStage[]? stage,
        CandidateSource? source,
        HrPortalDbContext db,
        IAccessContext accessContext,
        CancellationToken ct)
    {
        var query = db.Applications.AsNoTracking().VisibleTo(db, await accessContext.GetAsync(ct));
        if (requisitionId is { } requisition)
        {
            query = query.Where(a => a.RequisitionId == requisition);
        }

        if (stage is { Length: > 0 })
        {
            query = query.Where(a => stage.Contains(a.Stage));
        }

        if (source is { } s)
        {
            query = query.Where(a => a.Source == s);
        }

        return await CardsAsync(db, query, search, ct);
    }

    /// <summary>CT-09: people who already have this email or phone, so HR links to them instead of duplicating.</summary>
    private static async Task<List<DuplicateMatch>> DuplicatesAsync(DuplicateCheckRequest request, HrPortalDbContext db, CancellationToken ct) =>
        await FindDuplicatesAsync(db, request.Email, request.Phone, exceptPersonId: null, ct);

    internal static async Task<List<DuplicateMatch>> FindDuplicatesAsync(
        HrPortalDbContext db, string? email, string? phone, Guid? exceptPersonId, CancellationToken ct)
    {
        var normalisedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        var phoneKey = Person.PhoneKeyOf(phone);
        if (normalisedEmail is null && phoneKey is null)
        {
            return [];
        }

        return await db.People.AsNoTracking()
            .Where(p => p.Id != exceptPersonId && ((normalisedEmail != null && p.PersonalEmail == normalisedEmail) || (phoneKey != null && p.PhoneKey == phoneKey)))
            .OrderBy(p => p.GivenName)
            .Take(10)
            .Select(p => new DuplicateMatch(
                p.Id,
                p.GivenName + " " + p.FamilyName,
                p.PersonalEmail,
                p.Phone,
                normalisedEmail != null && p.PersonalEmail == normalisedEmail,
                phoneKey != null && p.PhoneKey == phoneKey,
                db.Employees.Any(e => e.PersonId == p.Id)))
            .ToListAsync(ct);
    }

    private static async Task<IResult> GetAsync(
        Guid personId, HrPortalDbContext db, IAccessContext accessContext, IAuditLog audit, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var (permissions, denied) = await PermissionsAsync(db, access, personId, ct);
        if (denied is not null)
        {
            return denied;
        }

        var detail = await DetailAsync(db, access, personId, permissions!, ct);
        if (detail.CurrentCtc is not null || detail.ExpectedCtc is not null)
        {
            await audit.RecordViewAsync(CompensationAuditType, personId.ToString(), ct);
        }

        return Results.Ok(detail);
    }

    private static async Task<IResult> UpdateAsync(
        Guid personId, CandidateRequest request, HrPortalDbContext db, IAccessContext accessContext, TimeProvider clock, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var (permissions, denied) = await PermissionsAsync(db, access, personId, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!permissions!.CanEdit)
        {
            return Results.Forbid();
        }

        var duplicates = await FindDuplicatesAsync(db, request.Email, request.Phone, personId, ct);
        if (duplicates.Count > 0)
        {
            return DuplicateConflict(duplicates);
        }

        var person = await db.People.SingleAsync(p => p.Id == personId, ct);
        person.Rename(request.GivenName, request.FamilyName);
        person.UpdateContact(request.Email, request.Phone);
        var profile = await db.CandidateProfiles.SingleOrDefaultAsync(c => c.PersonId == personId, ct);
        if (profile is null)
        {
            var country = await (from a in db.Applications where a.PersonId == personId join r in db.Requisitions on a.RequisitionId equals r.Id orderby a.AppliedAtUtc select r.CountryCode).FirstAsync(ct);
            profile = db.CandidateProfiles.Add(new CandidateProfile(personId, country, clock.GetUtcNow())).Entity;
        }

        await ApplyProfileAsync(db, profile, request, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await DetailAsync(db, access, personId, permissions, ct));
    }

    private static async Task<IResult> UploadCvAsync(
        Guid personId,
        IFormFile? file,
        HrPortalDbContext db,
        IAccessContext accessContext,
        ICurrentUser user,
        IFileStore files,
        ICvTextExtractor extractor,
        TimeProvider clock,
        CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var (permissions, denied) = await PermissionsAsync(db, access, personId, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!permissions!.CanEdit)
        {
            return Results.Forbid();
        }

        if (file is null)
        {
            throw new DomainValidationException("file", "Choose a CV to upload.");
        }

        await using var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        var upload = UploadPolicy.Validate(
            file.FileName, content.Length, content.GetBuffer().AsSpan(0, (int)Math.Min(content.Length, UploadPolicy.HeaderLength)), UploadPolicy.CvExtensions);
        content.Position = 0;

        // RB-04: the text is kept for full-text search; the file itself goes to blob storage.
        var text = extractor.Extract(new MemoryStream(content.ToArray(), writable: false), upload.ContentType);
        var key = await files.SaveAsync(CandidateCv.Container, upload.FileName, upload.ContentType, content, ct);
        var cv = new CandidateCv(personId, upload, key, user.DisplayName, clock.GetUtcNow(), text);
        db.CandidateCvs.Add(cv);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await files.DeleteAsync(CandidateCv.Container, key, CancellationToken.None);
            throw;
        }

        return Results.Created($"/api/candidates/{personId}/cvs/{cv.Id}", new CvDto(cv.Id, cv.FileName, cv.SizeBytes, cv.UploadedBy, cv.UploadedAtUtc));
    }

    /// <summary>Panelists need the CV to interview, so anyone who can see one of the person's applications may download it.</summary>
    private static async Task<IResult> DownloadCvAsync(
        Guid personId, Guid cvId, HrPortalDbContext db, IAccessContext accessContext, IFileStore files, IAuditLog audit, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var (_, denied) = await PermissionsAsync(db, access, personId, ct);
        if (denied is not null)
        {
            return denied;
        }

        var cv = await db.CandidateCvs.AsNoTracking().SingleOrDefaultAsync(c => c.Id == cvId && c.PersonId == personId, ct);
        if (cv is null)
        {
            return Results.NotFound();
        }

        // Every CV download is recorded, whoever downloads it.
        await audit.RecordViewAsync(CvDownloadAuditType, cv.Id.ToString(), ct);
        return Results.File(await files.OpenReadAsync(CandidateCv.Container, cv.StorageKey, ct), cv.ContentType, cv.FileName);
    }

    internal static IResult DuplicateConflict(List<DuplicateMatch> duplicates) =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "A person with this email or phone number already exists. Link the application to them instead of creating a duplicate.",
            extensions: new Dictionary<string, object?> { ["duplicates"] = duplicates });

    internal static async Task ApplyProfileAsync(HrPortalDbContext db, CandidateProfile profile, CandidateRequest request, CancellationToken ct) =>
        profile.Update(
            request.CurrentEmployer,
            request.NoticePeriodDays,
            ToMoney("currentCtc", request.CurrentCtc),
            ToMoney("expectedCtc", request.ExpectedCtc),
            await RecruitingAccess.CurrenciesAsync(db, ct));

    private static Money? ToMoney(string field, MoneyDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        try
        {
            return new Money(dto.Amount, (dto.Currency ?? string.Empty).Trim().ToUpperInvariant());
        }
        catch (ArgumentException)
        {
            throw new DomainValidationException(field, "Use a three-letter currency code (PKR or USD).");
        }
    }

    /// <param name="CanSeeContact">Hiring managers browsing the Resume Bank do not get contact details of candidates outside their requisitions.</param>
    private sealed record CandidatePermissions(bool CanEdit, bool CanSeeCompensation, bool CanSeeContact, IReadOnlyList<Guid> VisibleApplicationIds);

    /// <summary>
    /// A person is visible through the applications the user may see, or through the Resume Bank (HR in the
    /// candidate's country; hiring managers everyone). Permissions are the union.
    /// </summary>
    private static async Task<(CandidatePermissions? Permissions, IResult? Denied)> PermissionsAsync(
        HrPortalDbContext db, UserAccess access, Guid personId, CancellationToken ct)
    {
        if (!await db.People.AnyAsync(p => p.Id == personId, ct))
        {
            return (null, Results.NotFound());
        }

        var visible = await (
                from a in db.Applications.AsNoTracking().VisibleTo(db, access)
                where a.PersonId == personId
                join r in db.Requisitions on a.RequisitionId equals r.Id
                select new { a.Id, r.CountryCode, r.RequesterEmployeeId, r.HiringManagerEmployeeId })
            .ToListAsync(ct);
        var profileCountry = await db.CandidateProfiles.Where(c => c.PersonId == personId).Select(c => c.CountryCode).SingleOrDefaultAsync(ct);
        var resumeBank = profileCountry is null ? null : ResumeBankRules.For(access, profileCountry, hiringManagerOfAnApplication: false);
        if (visible.Count == 0 && resumeBank is not { CanView: true })
        {
            return (null, Results.Forbid());
        }

        var perApplication = new List<ApplicationPermissions>();
        foreach (var a in visible)
        {
            var panelist = access.EmployeeId is { } me && await RecruitingAccess.IsPanelistAsync(db, a.Id, me, ct);
            perApplication.Add(RecruitingRules.For(access, new ApplicationFacts(a.CountryCode, a.RequesterEmployeeId, a.HiringManagerEmployeeId, panelist)));
        }

        return (new CandidatePermissions(
                perApplication.Any(p => p.CanManage) || resumeBank?.CanEdit == true,
                perApplication.Any(p => p.CanSeeCompensation) || resumeBank?.CanSeeCompensation == true,
                visible.Count > 0 || resumeBank?.CanSeeContact == true,
                visible.Select(a => a.Id).ToList()),
            null);
    }

    private static async Task<CandidateDetail> DetailAsync(
        HrPortalDbContext db, UserAccess access, Guid personId, CandidatePermissions permissions, CancellationToken ct)
    {
        var person = await db.People.AsNoTracking().SingleAsync(p => p.Id == personId, ct);
        var profile = await db.CandidateProfiles.AsNoTracking().SingleOrDefaultAsync(c => c.PersonId == personId, ct);
        var applications = await CardsAsync(db, db.Applications.AsNoTracking().Where(a => permissions.VisibleApplicationIds.Contains(a.Id)), null, ct);
        var cvs = await db.CandidateCvs.AsNoTracking().Where(c => c.PersonId == personId).OrderByDescending(c => c.UploadedAtUtc)
            .Select(c => new CvDto(c.Id, c.FileName, c.SizeBytes, c.UploadedBy, c.UploadedAtUtc))
            .ToListAsync(ct);
        MoneyDto? Show(Money? money) => permissions.CanSeeCompensation && money is { } m ? new MoneyDto(m.Amount, m.Currency) : null;

        return new CandidateDetail(
            person.Id, person.GivenName, person.FamilyName,
            permissions.CanSeeContact ? person.PersonalEmail : null,
            permissions.CanSeeContact ? person.Phone : null,
            profile?.CurrentEmployer, profile?.NoticePeriodDays, Show(profile?.CurrentCtc), Show(profile?.ExpectedCtc),
            permissions.CanSeeCompensation, permissions.CanEdit, applications, cvs);
    }

    internal static async Task<List<ApplicationCard>> CardsAsync(HrPortalDbContext db, IQueryable<Application> query, string? search, CancellationToken ct)
    {
        var rows =
            from a in query
            join p in db.People on a.PersonId equals p.Id
            join r in db.Requisitions on a.RequisitionId equals r.Id
            from c in db.CandidateProfiles.Where(c => c.PersonId == p.Id).DefaultIfEmpty()
            select new { a, p, r, c };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal)}%";
            rows = rows.Where(x =>
                EF.Functions.ILike(x.p.GivenName + " " + x.p.FamilyName, pattern)
                || (x.p.PersonalEmail != null && EF.Functions.ILike(x.p.PersonalEmail, pattern))
                || (x.c != null && x.c.CurrentEmployer != null && EF.Functions.ILike(x.c.CurrentEmployer, pattern)));
        }

        return await rows
            .OrderByDescending(x => x.a.StageChangedAtUtc)
            .Select(x => new ApplicationCard(
                x.a.Id, x.p.Id, x.p.GivenName + " " + x.p.FamilyName, x.p.PersonalEmail, x.c == null ? null : x.c.CurrentEmployer,
                x.a.Stage, x.a.InterviewRound, x.a.Source, x.a.AppliedAtUtc, x.a.StageChangedAtUtc,
                x.r.Id, x.r.Number, x.r.Title, x.r.CountryCode,
                db.Interviews.Count(i => i.ApplicationId == x.a.Id && i.Status == InterviewStatus.Scheduled)))
            .ToListAsync(ct);
    }
}
