using HrPortal.Api.Auth;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Files;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Employees;
using HrPortal.Infrastructure.Persistence;
using HrPortal.Infrastructure.Recruiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Recruiting.CandidateEndpoints;

namespace HrPortal.Api.Recruiting;

/// <summary>
/// The Resume Bank (RB-01..06, RB-08, RB-12): every candidate ever considered, searchable by CV content and filters,
/// sharing the Candidate Tracker's person and candidate record. Access per <see cref="ResumeBankRules"/>.
/// </summary>
public static class ResumeBankEndpoints
{
    public sealed record ResumeBankRow(
        Guid PersonId,
        string Name,
        string CountryCode,
        string? Location,
        string? CurrentEmployer,
        decimal? ExperienceYears,
        int? NoticePeriodDays,
        IReadOnlyList<string> Skills,
        IReadOnlyList<string> Tags,
        ResumeBankStatus? Status,
        DateTimeOffset? LastContactedAtUtc,
        bool HasCv,
        int ApplicationCount,
        string? LatestOutcome,
        MoneyDto? CurrentCtc);

    public sealed record SearchResult(int Total, int Page, int PageSize, IReadOnlyList<ResumeBankRow> Items);

    /// <param name="Kind">applied, stage, interview, feedback, status, note, employment.</param>
    public sealed record TimelineItem(DateTimeOffset AtUtc, string Kind, string Title, string? Detail, Guid? ApplicationId);

    public sealed record ApplicationOutcome(
        Guid ApplicationId, string RequisitionNumber, string RequisitionTitle, string CountryCode, PipelineStage Stage, int? InterviewRound, DateTimeOffset AppliedAtUtc, bool CanOpen);

    public sealed record ResumeBankDetail(
        Guid PersonId,
        string Name,
        string? Email,
        string? Phone,
        string CountryCode,
        string? Location,
        string? CurrentEmployer,
        decimal? ExperienceYears,
        int? NoticePeriodDays,
        IReadOnlyList<string> Skills,
        IReadOnlyList<string> Tags,
        ResumeBankStatus? Status,
        string? BlacklistReason,
        DateTimeOffset? LastContactedAtUtc,
        MoneyDto? CurrentCtc,
        MoneyDto? ExpectedCtc,
        bool IsEmployee,
        IReadOnlyList<CvDto> Cvs,
        IReadOnlyList<ApplicationOutcome> Applications,
        IReadOnlyList<TimelineItem> Timeline,
        ResumeBankPermissions Permissions);

    public sealed record DetailsRequest(string CountryCode, string? Location, decimal? ExperienceYears, IReadOnlyList<string>? Skills, IReadOnlyList<string>? Tags);

    public sealed record StatusRequest(ResumeBankStatus? Status, string? Reason);

    public sealed record ReasonRequest(string? Reason);

    public sealed record NoteRequest(string Body);

    public static IEndpointRouteBuilder MapResumeBankEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resume-bank").WithTags("Resume Bank");
        var view = Policies.Module(HrModule.ResumeBank, Capability.View);
        var edit = Policies.Module(HrModule.ResumeBank, Capability.Edit);
        var create = Policies.Module(HrModule.ResumeBank, Capability.Create);

        group.MapGet("/", SearchAsync).RequireAuthorization(view);
        group.MapGet("/{personId:guid}", GetAsync).RequireAuthorization(view);
        group.MapPut("/{personId:guid}/details", UpdateDetailsAsync).RequireAuthorization(edit);
        group.MapPost("/{personId:guid}/status", SetStatusAsync).RequireAuthorization(edit);
        group.MapPost("/{personId:guid}/blacklist", BlacklistAsync).RequireAuthorization(edit);
        group.MapPost("/{personId:guid}/unblacklist", UnblacklistAsync).RequireAuthorization(edit);
        group.MapPost("/{personId:guid}/notes", AddNoteAsync).RequireAuthorization(edit);
        group.MapPost("/{personId:guid}/contacted", ContactedAsync).RequireAuthorization(edit);

        var import = group.MapGroup("/import").RequireAuthorization(create);
        import.MapGet("/template", async (CandidateImporter importer, CancellationToken ct) =>
            Results.File(await importer.TemplateAsync(ct), XlsxContentType, "candidate-import-template.xlsx"));
        import.MapPost("/", async (IFormFile? file, CandidateImporter importer, IAccessContext accessContext, ICurrentUser user, CancellationToken ct) =>
            {
                var access = await accessContext.GetAsync(ct);
                return Results.Ok(await importer.ImportAsync(await ReadAsync(file, ct), access, ActorOf(user, access), ct));
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadPolicy.MaxBytes + 64 * 1024));
        import.MapPost("/error-report", async (IFormFile? file, CandidateImporter importer, IAccessContext accessContext, ICurrentUser user, CancellationToken ct) =>
            {
                var access = await accessContext.GetAsync(ct);
                var report = await importer.ErrorReportAsync(await ReadAsync(file, ct), access, ActorOf(user, access), ct);
                return Results.File(report, XlsxContentType, "candidate-import-errors.xlsx");
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadPolicy.MaxBytes + 64 * 1024));
        return app;
    }

    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>RB-04: full-text search inside CVs plus filters, paged. Salary filtering is for HR only.</summary>
    private static async Task<IResult> SearchAsync(
        string? q,
        string? skills,
        decimal? minExperience,
        decimal? maxExperience,
        string? location,
        decimal? maxCurrentCtc,
        string? ctcCurrency,
        int? maxNoticeDays,
        ResumeBankStatus? status,
        DateOnly? contactedSince,
        string? tag,
        string? country,
        int? page,
        int? pageSize,
        HrPortalDbContext db,
        IAccessContext accessContext,
        CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var filters = new ResumeBankFilters(
            q, (skills ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            minExperience, maxExperience, location, maxNoticeDays, status, contactedSince, tag, country);
        var query = db.CandidateProfiles.AsNoTracking().VisibleTo(access).Search(db, filters);
        var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var number = Math.Max(page ?? 1, 1);

        var ordered = query.OrderByDescending(p => p.LastContactedAtUtc.HasValue).ThenByDescending(p => p.LastContactedAtUtc).ThenBy(p => p.PersonId);
        List<Guid> pageIds;
        int total;
        if (maxCurrentCtc is { } limit)
        {
            // Salary is encrypted, so it is compared after decryption, on candidates the user may see salary for.
            var currency = (ctcCurrency ?? string.Empty).Trim().ToUpperInvariant();
            var candidates = await ordered.Select(p => new { p.PersonId, p.CountryCode, p.CurrentCtcAmount, p.CurrentCtcCurrency }).ToListAsync(ct);
            var matches = candidates
                .Where(p => ResumeBankRules.IsHrFor(access, p.CountryCode, Capability.View)
                    && p.CurrentCtcAmount is not null && p.CurrentCtcCurrency == currency
                    && decimal.Parse(p.CurrentCtcAmount, System.Globalization.CultureInfo.InvariantCulture) <= limit)
                .Select(p => p.PersonId)
                .ToList();
            total = matches.Count;
            pageIds = matches.Skip((number - 1) * size).Take(size).ToList();
        }
        else
        {
            total = await query.CountAsync(ct);
            pageIds = await ordered.Skip((number - 1) * size).Take(size).Select(p => p.PersonId).ToListAsync(ct);
        }

        return Results.Ok(new SearchResult(total, number, size, await RowsAsync(db, access, pageIds, ct)));
    }

    private static async Task<List<ResumeBankRow>> RowsAsync(HrPortalDbContext db, UserAccess access, List<Guid> personIds, CancellationToken ct)
    {
        var rows = await (
                from p in db.CandidateProfiles.AsNoTracking()
                where personIds.Contains(p.PersonId)
                join person in db.People on p.PersonId equals person.Id
                select new
                {
                    Profile = p,
                    Name = person.GivenName + " " + person.FamilyName,
                    HasCv = db.CandidateCvs.Any(c => c.PersonId == p.PersonId),
                    Applications = db.Applications.Count(a => a.PersonId == p.PersonId),
                    Latest = (from a in db.Applications
                              where a.PersonId == p.PersonId
                              join r in db.Requisitions on a.RequisitionId equals r.Id
                              orderby a.StageChangedAtUtc descending
                              select new { r.Number, a.Stage, a.InterviewRound }).FirstOrDefault(),
                })
            .ToListAsync(ct);

        return personIds.Select(id => rows.Single(r => r.Profile.PersonId == id)).Select(r =>
        {
            var p = r.Profile;
            var hr = ResumeBankRules.IsHrFor(access, p.CountryCode, Capability.View);
            return new ResumeBankRow(
                p.PersonId, r.Name, p.CountryCode, p.Location, p.CurrentEmployer, p.ExperienceYears, p.NoticePeriodDays, p.Skills, p.Tags,
                p.Status, p.LastContactedAtUtc, r.HasCv, r.Applications,
                r.Latest is null ? null : $"{r.Latest.Number}: {Application.Label(r.Latest.Stage)}{(r.Latest.InterviewRound is { } round ? $" {round}" : string.Empty)}",
                hr && p.CurrentCtc is { } ctc ? new MoneyDto(ctc.Amount, ctc.Currency) : null);
        }).ToList();
    }

    private static async Task<IResult> GetAsync(Guid personId, HrPortalDbContext db, IAccessContext accessContext, IAuditLog audit, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var (context, denied) = await LoadAsync(db, access, personId, ct);
        if (denied is not null)
        {
            return denied;
        }

        var detail = await DetailAsync(db, access, context!, ct);
        if (detail.CurrentCtc is not null || detail.ExpectedCtc is not null)
        {
            await audit.RecordViewAsync(CompensationAuditType, personId.ToString(), ct);
        }

        return Results.Ok(detail);
    }

    private static Task<IResult> UpdateDetailsAsync(
        Guid personId, DetailsRequest request, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct) =>
        ActAsync(db, accessContext, personId, p => p.CanEdit, (context, access, _) =>
        {
            // Moving a candidate to another country must stay within the HR user's countries.
            var country = (request.CountryCode ?? string.Empty).Trim().ToUpperInvariant();
            if (!ResumeBankRules.IsHrFor(access, country, Capability.Edit))
            {
                return Task.FromResult<IResult?>(Results.Forbid());
            }

            context.Profile.UpdateResumeDetails(country, request.Location, request.ExperienceYears, request.Skills ?? [], request.Tags ?? []);
            return Task.FromResult<IResult?>(null);
        }, ct);

    private static Task<IResult> SetStatusAsync(
        Guid personId, StatusRequest request, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        ActAsync(db, accessContext, personId, p => p.CanEdit, (context, access, _) =>
        {
            context.Profile.SetStatus(request.Status, request.Reason, ActorOf(user, access), clock.GetUtcNow());
            return Task.FromResult<IResult?>(null);
        }, ct);

    /// <summary>HR Admins only; the reason is required and kept, and the change is audited.</summary>
    private static Task<IResult> BlacklistAsync(
        Guid personId, ReasonRequest request, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        ActAsync(db, accessContext, personId, p => p.CanBlacklist, (context, access, _) =>
        {
            context.Profile.Blacklist(request.Reason ?? string.Empty, ActorOf(user, access), clock.GetUtcNow());
            return Task.FromResult<IResult?>(null);
        }, ct);

    private static Task<IResult> UnblacklistAsync(
        Guid personId, ReasonRequest request, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        ActAsync(db, accessContext, personId, p => p.CanBlacklist, (context, access, _) =>
        {
            context.Profile.RemoveFromBlacklist(request.Reason ?? string.Empty, ActorOf(user, access), clock.GetUtcNow());
            return Task.FromResult<IResult?>(null);
        }, ct);

    private static Task<IResult> AddNoteAsync(
        Guid personId, NoteRequest request, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        ActAsync(db, accessContext, personId, p => p.CanEdit, (context, access, _) =>
        {
            db.CandidateNotes.Add(new CandidateNote(personId, request.Body, ActorOf(user, access), clock.GetUtcNow()));
            return Task.FromResult<IResult?>(null);
        }, ct);

    private static Task<IResult> ContactedAsync(Guid personId, HrPortalDbContext db, IAccessContext accessContext, TimeProvider clock, CancellationToken ct) =>
        ActAsync(db, accessContext, personId, p => p.CanEdit, (context, _, _) =>
        {
            context.Profile.MarkContacted(clock.GetUtcNow());
            return Task.FromResult<IResult?>(null);
        }, ct);

    private sealed record CandidateContext(CandidateProfile Profile, Person Person, ResumeBankPermissions Permissions, IReadOnlySet<Guid> OwnApplicationIds);

    /// <summary>
    /// Loads the candidate and the user's permissions. Applications on requisitions the user is hiring manager for
    /// give them salary and those applications' notes (Candidate Tracker rules).
    /// </summary>
    private static async Task<(CandidateContext? Context, IResult? Denied)> LoadAsync(HrPortalDbContext db, UserAccess access, Guid personId, CancellationToken ct)
    {
        var profile = await db.CandidateProfiles.Include(p => p.StatusHistory).SingleOrDefaultAsync(p => p.PersonId == personId, ct);
        if (profile is null)
        {
            return (null, Results.NotFound());
        }

        var applications = await (
                from a in db.Applications
                where a.PersonId == personId
                join r in db.Requisitions on a.RequisitionId equals r.Id
                select new { a.Id, r.RequesterEmployeeId, r.HiringManagerEmployeeId })
            .ToListAsync(ct);
        var own = applications
            .Where(a => RecruitingRules.IsHiringManagerFor(access, a.RequesterEmployeeId, a.HiringManagerEmployeeId))
            .Select(a => a.Id)
            .ToHashSet();
        var permissions = ResumeBankRules.For(access, profile.CountryCode, own.Count > 0);
        if (!permissions.CanView)
        {
            return (null, Results.Forbid());
        }

        var person = await db.People.SingleAsync(p => p.Id == personId, ct);
        return (new CandidateContext(profile, person, permissions, own), null);
    }

    private static async Task<IResult> ActAsync(
        HrPortalDbContext db,
        IAccessContext accessContext,
        Guid personId,
        Func<ResumeBankPermissions, bool> allowed,
        Func<CandidateContext, UserAccess, CancellationToken, Task<IResult?>> action,
        CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var (context, denied) = await LoadAsync(db, access, personId, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!allowed(context!.Permissions))
        {
            return Results.Forbid();
        }

        if (await action(context, access, ct) is { } early)
        {
            return early;
        }

        await db.SaveChangesAsync(ct);
        var (reloaded, _) = await LoadAsync(db, access, personId, ct);
        return Results.Ok(await DetailAsync(db, access, reloaded!, ct));
    }

    private static async Task<ResumeBankDetail> DetailAsync(HrPortalDbContext db, UserAccess access, CandidateContext context, CancellationToken ct)
    {
        var (profile, person, permissions, own) = context;
        var applications = await (
                from a in db.Applications.AsNoTracking().Include(a => a.History)
                where a.PersonId == person.Id
                join r in db.Requisitions on a.RequisitionId equals r.Id
                orderby a.AppliedAtUtc descending
                select new { Application = a, Requisition = r })
            .ToListAsync(ct);

        // Per application: may the user open it in the Candidate Tracker, and see its feedback and free-text reasons?
        var insider = new Dictionary<Guid, bool>();
        var canOpen = new Dictionary<Guid, bool>();
        foreach (var x in applications)
        {
            var panelist = access.EmployeeId is { } me && await RecruitingAccess.IsPanelistAsync(db, x.Application.Id, me, ct);
            var rules = RecruitingRules.For(access, new ApplicationFacts(x.Requisition.CountryCode, x.Requisition.RequesterEmployeeId, x.Requisition.HiringManagerEmployeeId, panelist));
            insider[x.Application.Id] = rules.CanSeeAllFeedback;
            canOpen[x.Application.Id] = rules.CanView;
        }

        var timeline = new List<TimelineItem>();
        foreach (var x in applications)
        {
            var (a, r) = (x.Application, x.Requisition);
            var label = $"{r.Number} {r.Title}";
            timeline.Add(new TimelineItem(a.AppliedAtUtc, "applied", $"Applied to {label}", $"Source: {a.Source}", a.Id));
            foreach (var h in a.History.Where(h => h.FromStage is not null))
            {
                var detail = string.Join(": ", new[] { h.RejectionReasonName, insider[a.Id] ? h.Reason : null }.Where(t => !string.IsNullOrWhiteSpace(t)));
                timeline.Add(new TimelineItem(
                    h.AtUtc, "stage", $"{label}: {Application.Label(h.ToStage)}{(h.ToRound is { } round ? $" {round}" : string.Empty)}",
                    detail.Length == 0 ? null : detail, a.Id));
            }
        }

        var applicationIds = applications.Select(x => x.Application.Id).ToList();
        var interviews = await db.Interviews.AsNoTracking().Where(i => applicationIds.Contains(i.ApplicationId)).ToListAsync(ct);
        foreach (var i in interviews.Where(i => i.Status == InterviewStatus.Scheduled))
        {
            var r = applications.Single(x => x.Application.Id == i.ApplicationId).Requisition;
            timeline.Add(new TimelineItem(i.StartUtc, "interview", $"{r.Number}: interview round {i.Round}", i.Mode.ToString(), i.ApplicationId));
        }

        // Feedback only where the user may see it in the Candidate Tracker (CT-07 rules apply there too).
        var insiderInterviews = interviews.Where(i => insider[i.ApplicationId]).Select(i => i.Id).ToList();
        var scorecards = await (
                from s in db.Scorecards.AsNoTracking()
                where insiderInterviews.Contains(s.InterviewId)
                join e in db.Employees on s.InterviewerEmployeeId equals e.Id
                select new { s.InterviewId, s.Recommendation, s.SubmittedAtUtc, Name = e.Person.GivenName + " " + e.Person.FamilyName })
            .ToListAsync(ct);
        foreach (var s in scorecards)
        {
            var interview = interviews.Single(i => i.Id == s.InterviewId);
            var r = applications.Single(x => x.Application.Id == interview.ApplicationId).Requisition;
            timeline.Add(new TimelineItem(s.SubmittedAtUtc, "feedback", $"{r.Number} round {interview.Round}: {s.Name} recommends {s.Recommendation}", null, interview.ApplicationId));
        }

        foreach (var change in profile.StatusHistory)
        {
            timeline.Add(new TimelineItem(
                change.AtUtc, "status", $"Resume Bank status: {(change.ToStatus is { } to ? to.ToString() : "cleared")}",
                permissions.CanSeeNotes ? change.Reason : null, null));
        }

        if (permissions.CanSeeNotes)
        {
            foreach (var note in await db.CandidateNotes.AsNoTracking().Where(n => n.PersonId == person.Id).ToListAsync(ct))
            {
                timeline.Add(new TimelineItem(note.CreatedAtUtc, "note", $"Note by {note.AuthorName}", note.Body, null));
            }

            foreach (var employee in await db.Employees.AsNoTracking().Where(e => e.PersonId == person.Id).ToListAsync(ct))
            {
                timeline.Add(new TimelineItem(
                    new DateTimeOffset(employee.JoiningDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), "employment",
                    $"Joined as {employee.Designation} ({employee.EmployeeNumber})", null, null));
            }
        }

        var cvs = await db.CandidateCvs.AsNoTracking().Where(c => c.PersonId == person.Id).OrderByDescending(c => c.UploadedAtUtc)
            .Select(c => new CvDto(c.Id, c.FileName, c.SizeBytes, c.UploadedBy, c.UploadedAtUtc)).ToListAsync(ct);
        MoneyDto? Show(Money? money) => permissions.CanSeeCompensation && money is { } m ? new MoneyDto(m.Amount, m.Currency) : null;

        return new ResumeBankDetail(
            person.Id, person.FullName,
            permissions.CanSeeContact ? person.PersonalEmail : null,
            permissions.CanSeeContact ? person.Phone : null,
            profile.CountryCode, profile.Location, profile.CurrentEmployer, profile.ExperienceYears, profile.NoticePeriodDays,
            profile.Skills, profile.Tags, profile.Status,
            permissions.CanSeeNotes ? profile.BlacklistReason : null,
            profile.LastContactedAtUtc, Show(profile.CurrentCtc), Show(profile.ExpectedCtc),
            await db.Employees.AnyAsync(e => e.PersonId == person.Id, ct),
            cvs,
            applications.Select(x => new ApplicationOutcome(
                x.Application.Id, x.Requisition.Number, x.Requisition.Title, x.Requisition.CountryCode, x.Application.Stage, x.Application.InterviewRound,
                x.Application.AppliedAtUtc, canOpen[x.Application.Id])).ToList(),
            timeline.OrderByDescending(t => t.AtUtc).ToList(),
            permissions);
    }

    private static Actor ActorOf(ICurrentUser user, UserAccess access) => new(user.UserId, user.DisplayName, access.EmployeeId);

    private static async Task<Sheet> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new DomainValidationException("file", "Choose an .xlsx or .csv file.");
        }

        if (file.Length > UploadPolicy.MaxBytes)
        {
            throw new DomainValidationException("file", "Files can be at most 10 MB.");
        }

        await using var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        content.Position = 0;
        return SpreadsheetReader.Read(content, file.FileName);
    }
}
