using HrPortal.Api.Auth;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Notifications;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using HrPortal.Infrastructure.Recruiting;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Recruiting.CandidateEndpoints;

namespace HrPortal.Api.Recruiting;

/// <summary>
/// The pipeline per requisition (CT-01, CT-03), interviews (recorded manually for now; Outlook invites in Phase 2),
/// scorecards (CT-06, CT-07) and notes (CT-08). Access per <see cref="RecruitingRules"/>.
/// </summary>
public static class ApplicationEndpoints
{
    public sealed record BoardResponse(
        Guid RequisitionId,
        string Number,
        string Title,
        RequisitionStatus Status,
        string CountryCode,
        int InterviewRounds,
        IReadOnlyList<string> ScorecardCriteria,
        bool CanManage,
        bool CanAddCandidates,
        IReadOnlyList<ApplicationCard> Applications);

    /// <summary>Either link an existing person (CT-09) or describe a new candidate.</summary>
    public sealed record AddApplicationRequest(Guid? PersonId, CandidateRequest? Candidate, CandidateSource Source);

    public sealed record StageRequest(PipelineStage Stage, int? Round, Guid? RejectionReasonId, string? Reason);

    public sealed record NoteRequest(string Body);

    public sealed record ScheduleInterviewRequest(
        int Round, DateTimeOffset StartUtc, int DurationMinutes, InterviewMode Mode, string? Location, IReadOnlyList<Guid> PanelEmployeeIds);

    public sealed record ScorecardRequest(IReadOnlyList<CriterionRating> Ratings, Recommendation Recommendation, string? Comment);

    public sealed record StageChangeDto(
        PipelineStage? FromStage, int? FromRound, PipelineStage ToStage, int? ToRound, string? RejectionReason, string? Reason, string ActorName, DateTimeOffset AtUtc);

    public sealed record NoteDto(Guid Id, string Body, string AuthorName, DateTimeOffset CreatedAtUtc);

    public sealed record ScorecardDto(string InterviewerName, IReadOnlyList<CriterionRating> Ratings, Recommendation Recommendation, string? Comment, DateTimeOffset SubmittedAtUtc);

    /// <param name="FeedbackHidden">The viewer is on this panel and has not submitted yet, so others' feedback is withheld (CT-07).</param>
    public sealed record InterviewDto(
        Guid Id,
        int Round,
        DateTimeOffset StartUtc,
        int DurationMinutes,
        InterviewMode Mode,
        string? Location,
        InterviewStatus Status,
        IReadOnlyList<string> PanelNames,
        IReadOnlyList<string> Criteria,
        int SubmittedCount,
        bool IsOnPanel,
        bool MyScorecardSubmitted,
        bool FeedbackHidden,
        IReadOnlyList<ScorecardDto> Scorecards);

    public sealed record CandidateSummary(
        Guid PersonId, string Name, string? Email, string? Phone, string? CurrentEmployer, int? NoticePeriodDays, MoneyDto? CurrentCtc, MoneyDto? ExpectedCtc);

    public sealed record RequisitionRef(Guid Id, string Number, string Title, RequisitionStatus Status, int InterviewRounds, IReadOnlyList<string> ScorecardCriteria);

    public sealed record ApplicationDetail(
        Guid Id,
        PipelineStage Stage,
        int? InterviewRound,
        CandidateSource Source,
        DateTimeOffset AppliedAtUtc,
        CandidateSummary Candidate,
        RequisitionRef Requisition,
        IReadOnlyList<StageChangeDto> History,
        IReadOnlyList<NoteDto> Notes,
        IReadOnlyList<InterviewDto> Interviews,
        IReadOnlyList<CvDto> Cvs,
        ApplicationPermissions Permissions);

    public sealed record MyInterview(
        Guid InterviewId,
        Guid ApplicationId,
        string CandidateName,
        string RequisitionNumber,
        string RequisitionTitle,
        int Round,
        DateTimeOffset StartUtc,
        int DurationMinutes,
        InterviewMode Mode,
        string? Location,
        bool ScorecardSubmitted);

    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/requisitions/{id:guid}/applications", BoardAsync).WithTags("Recruiting").RequireAuthorization(Policies.Authenticated);
        app.MapPost("/api/requisitions/{id:guid}/applications", AddAsync).WithTags("Recruiting")
            .RequireAuthorization(Policies.Module(HrModule.Candidates, Capability.Create));

        var group = app.MapGroup("/api/applications/{id:guid}").WithTags("Recruiting");
        group.MapGet("/", GetAsync).RequireAuthorization(Policies.Authenticated);
        group.MapPost("/stage", MoveAsync).RequireAuthorization(Policies.Module(HrModule.Candidates, Capability.Edit));
        group.MapPost("/notes", AddNoteAsync).RequireAuthorization(Policies.Authenticated);
        group.MapPost("/interviews", ScheduleAsync).RequireAuthorization(Policies.Module(HrModule.Candidates, Capability.Edit));
        group.MapDelete("/interviews/{interviewId:guid}", CancelInterviewAsync).RequireAuthorization(Policies.Module(HrModule.Candidates, Capability.Edit));

        app.MapPost("/api/interviews/{interviewId:guid}/scorecard", SubmitScorecardAsync).WithTags("Recruiting").RequireAuthorization(Policies.Authenticated);
        app.MapGet("/api/interviews/mine", MyInterviewsAsync).WithTags("Recruiting").RequireAuthorization(Policies.Authenticated);
        return app;
    }

    private static async Task<IResult> BoardAsync(Guid id, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var requisition = await db.Requisitions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        if (requisition is null)
        {
            return Results.NotFound();
        }

        var visible = db.Applications.AsNoTracking().Where(a => a.RequisitionId == id).VisibleTo(db, access);
        var insider = RecruitingRules.IsHrFor(access, requisition.CountryCode, Capability.View)
            || RecruitingRules.IsHiringManagerFor(access, requisition.RequesterEmployeeId, requisition.HiringManagerEmployeeId);
        if (!insider && !await visible.AnyAsync(ct))
        {
            return Results.Forbid();
        }

        var canManage = RecruitingRules.IsHrFor(access, requisition.CountryCode, Capability.Edit);
        return Results.Ok(new BoardResponse(
            requisition.Id, requisition.Number, requisition.Title, requisition.Status, requisition.CountryCode,
            requisition.InterviewRounds, requisition.ScorecardCriteria, canManage,
            RecruitingRules.IsHrFor(access, requisition.CountryCode, Capability.Create) && requisition.AcceptsCandidates,
            await CardsAsync(db, visible, null, ct)));
    }

    private static async Task<IResult> AddAsync(
        Guid id, AddApplicationRequest request, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, TimeProvider clock, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var requisition = await db.Requisitions.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (requisition is null)
        {
            return Results.NotFound();
        }

        if (!RecruitingRules.IsHrFor(access, requisition.CountryCode, Capability.Create))
        {
            return Results.Forbid();
        }

        var now = clock.GetUtcNow();
        Guid personId;
        if (request.PersonId is { } existing)
        {
            if (!await db.People.AnyAsync(p => p.Id == existing, ct))
            {
                throw new DomainValidationException("personId", "That person does not exist.");
            }

            personId = existing;
            if (!await db.CandidateProfiles.AnyAsync(c => c.PersonId == existing, ct))
            {
                db.CandidateProfiles.Add(new CandidateProfile(existing, now));
            }
        }
        else
        {
            var candidate = request.Candidate ?? throw new DomainValidationException("candidate", "Enter the candidate's details or choose an existing person.");
            var duplicates = await FindDuplicatesAsync(db, candidate.Email, candidate.Phone, null, ct);
            if (duplicates.Count > 0)
            {
                return DuplicateConflict(duplicates);
            }

            var person = new Person(candidate.GivenName, candidate.FamilyName);
            person.UpdateContact(candidate.Email, candidate.Phone);
            var profile = new CandidateProfile(person.Id, now);
            await ApplyProfileAsync(db, profile, candidate, ct);
            db.People.Add(person);
            db.CandidateProfiles.Add(profile);
            personId = person.Id;
        }

        if (await db.Applications.AnyAsync(a => a.RequisitionId == id && a.PersonId == personId, ct))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This person has already applied to this requisition.");
        }

        var application = Application.Create(personId, requisition, request.Source, new Actor(user.UserId, user.DisplayName, access.EmployeeId), now);
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);
        var card = (await CardsAsync(db, db.Applications.Where(a => a.Id == application.Id), null, ct)).Single();
        return Results.Created($"/api/applications/{application.Id}", card);
    }

    private static async Task<IResult> GetAsync(Guid id, HrPortalDbContext db, IAccessContext accessContext, IAuditLog audit, CancellationToken ct)
    {
        var user = await accessContext.GetAsync(ct);
        var (access, denied) = await RecruitingAccess.LoadAsync(db, user, id, ct);
        if (denied is not null)
        {
            return denied;
        }

        var detail = await DetailAsync(db, user, access!, ct);
        if (detail.Candidate.CurrentCtc is not null || detail.Candidate.ExpectedCtc is not null)
        {
            await audit.RecordViewAsync(CompensationAuditType, detail.Candidate.PersonId.ToString(), ct);
        }

        return Results.Ok(detail);
    }

    /// <summary>CT-01/03: drag-and-drop or "Move to"; logged with user, time and reason.</summary>
    private static async Task<IResult> MoveAsync(
        Guid id, StageRequest request, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        await ActAsync(db, accessContext, id, p => p.CanManage, async (access, userAccess) =>
        {
            var reason = request.RejectionReasonId is { } reasonId
                ? await db.RejectionReasons.SingleOrDefaultAsync(r => r.Id == reasonId, ct)
                    ?? throw new DomainValidationException("rejectionReasonId", "Choose a rejection reason from the list.")
                : null;
            access.Application.MoveTo(
                request.Stage, request.Round, reason, request.Reason, access.Requisition.InterviewRounds,
                new Actor(user.UserId, user.DisplayName, userAccess.EmployeeId), clock.GetUtcNow());
        }, ct);

    private static async Task<IResult> AddNoteAsync(
        Guid id, NoteRequest request, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        await ActAsync(db, accessContext, id, p => p.CanSeeNotes, (access, userAccess) =>
        {
            db.ApplicationNotes.Add(new ApplicationNote(access.Application.Id, request.Body, new Actor(user.UserId, user.DisplayName, userAccess.EmployeeId), clock.GetUtcNow()));
            return Task.CompletedTask;
        }, ct);

    /// <summary>
    /// Records an interview and creates it through <see cref="ICalendarService"/> (only logged locally until Outlook
    /// invites in Phase 2). Panelists are notified in-app and by email.
    /// </summary>
    private static async Task<IResult> ScheduleAsync(
        Guid id,
        ScheduleInterviewRequest request,
        HrPortalDbContext db,
        IAccessContext accessContext,
        ICalendarService calendar,
        INotificationEmailQueue emails,
        TimeProvider clock,
        CancellationToken ct)
    {
        var user = await accessContext.GetAsync(ct);
        var (access, denied) = await RecruitingAccess.LoadAsync(db, user, id, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!access!.Permissions.CanManage)
        {
            return Results.Forbid();
        }

        var panelIds = (request.PanelEmployeeIds ?? []).Distinct().ToList();
        var panel = await db.Employees.Include(e => e.Person).Where(e => panelIds.Contains(e.Id)).ToListAsync(ct);
        if (panel.Count != panelIds.Count)
        {
            throw new DomainValidationException("panelEmployeeIds", "Choose existing employees for the panel.");
        }

        var interview = Interview.Schedule(
            access.Application, request.Round, access.Requisition.InterviewRounds, request.StartUtc, request.DurationMinutes,
            request.Mode, request.Location, panel, access.Requisition.ScorecardCriteria);
        var candidate = await db.People.AsNoTracking().SingleAsync(p => p.Id == access.Application.PersonId, ct);
        var now = clock.GetUtcNow();
        var subject = $"Interview round {interview.Round}: {candidate.FullName} for {access.Requisition.Number} {access.Requisition.Title}";
        var notifications = panel.Select(p => new Notification(
                p.Id,
                $"Interview panel: {candidate.FullName}",
                $"You are on the round {interview.Round} panel for {candidate.FullName} ({access.Requisition.Number} {access.Requisition.Title}) " +
                $"on {interview.StartUtc:dd MMM yyyy HH:mm} UTC, {interview.DurationMinutes} minutes, {interview.Mode}." +
                (interview.Location is null ? string.Empty : $"\nWhere: {interview.Location}") +
                "\nSubmit your scorecard after the interview; you will see the panel's feedback once you have.",
                $"/applications/{access.Application.Id}",
                now))
            .ToList();
        db.Interviews.Add(interview);
        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync(ct);

        var organizer = user.EmployeeId is { } me
            ? await db.Employees.Where(e => e.Id == me).Select(e => e.WorkEmail).SingleAsync(ct)
            : "hr-portal@localhost";
        var attendees = panel.Select(p => p.WorkEmail).Concat(candidate.PersonalEmail is { } email ? [email] : []).ToList();
        var eventId = await calendar.CreateEventAsync(
            new CalendarEvent(
                organizer, attendees, subject,
                $"<p>{System.Net.WebUtility.HtmlEncode(subject)}</p><p>{System.Net.WebUtility.HtmlEncode(interview.Location ?? string.Empty)}</p>",
                interview.StartUtc, interview.StartUtc.AddMinutes(interview.DurationMinutes), interview.Mode == InterviewMode.Teams),
            ct);
        interview.RecordCalendarEvent(organizer, eventId);
        await db.SaveChangesAsync(ct);

        foreach (var notification in notifications)
        {
            emails.Enqueue(notification.Id);
        }

        return Results.Ok(await DetailAsync(db, user, access, ct));
    }

    private static async Task<IResult> CancelInterviewAsync(
        Guid id, Guid interviewId, HrPortalDbContext db, IAccessContext accessContext, ICalendarService calendar, CancellationToken ct)
    {
        var user = await accessContext.GetAsync(ct);
        var (access, denied) = await RecruitingAccess.LoadAsync(db, user, id, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!access!.Permissions.CanManage)
        {
            return Results.Forbid();
        }

        var interview = await db.Interviews.SingleOrDefaultAsync(i => i.Id == interviewId && i.ApplicationId == id, ct);
        if (interview is null)
        {
            return Results.NotFound();
        }

        interview.Cancel();
        await db.SaveChangesAsync(ct);
        if (interview is { CalendarEventId: { } eventId, OrganizerEmail: { } organizer })
        {
            await calendar.CancelEventAsync(organizer, eventId, ct);
        }

        return Results.Ok(await DetailAsync(db, user, access, ct));
    }

    /// <summary>CT-06: a panelist submits feedback once; afterwards they can see the rest of the panel's (CT-07).</summary>
    private static async Task<IResult> SubmitScorecardAsync(
        Guid interviewId, ScorecardRequest request, HrPortalDbContext db, IAccessContext accessContext, TimeProvider clock, CancellationToken ct)
    {
        var user = await accessContext.GetAsync(ct);
        var interview = await db.Interviews.Include(i => i.Panel).SingleOrDefaultAsync(i => i.Id == interviewId, ct);
        if (interview is null)
        {
            return Results.NotFound();
        }

        if (user.EmployeeId is not { } me || !interview.HasPanelist(me))
        {
            return Results.Forbid();
        }

        if (await db.Scorecards.AnyAsync(s => s.InterviewId == interviewId && s.InterviewerEmployeeId == me, ct))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "You have already submitted feedback for this interview; it cannot be changed.");
        }

        db.Scorecards.Add(Scorecard.Submit(interview, me, request.Ratings ?? [], request.Recommendation, request.Comment, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        var (access, _) = await RecruitingAccess.LoadAsync(db, user, interview.ApplicationId, ct);
        return Results.Ok(await DetailAsync(db, user, access!, ct));
    }

    private static async Task<List<MyInterview>> MyInterviewsAsync(HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct)
    {
        if ((await accessContext.GetAsync(ct)).EmployeeId is not { } me)
        {
            return [];
        }

        return await (
                from i in db.Interviews.AsNoTracking()
                where i.Status == InterviewStatus.Scheduled && i.Panel.Any(p => p.EmployeeId == me)
                join a in db.Applications on i.ApplicationId equals a.Id
                join p in db.People on a.PersonId equals p.Id
                join r in db.Requisitions on a.RequisitionId equals r.Id
                orderby i.StartUtc
                select new MyInterview(
                    i.Id, a.Id, p.GivenName + " " + p.FamilyName, r.Number, r.Title, i.Round, i.StartUtc, i.DurationMinutes, i.Mode, i.Location,
                    db.Scorecards.Any(s => s.InterviewId == i.Id && s.InterviewerEmployeeId == me)))
            .ToListAsync(ct);
    }

    private static async Task<IResult> ActAsync(
        HrPortalDbContext db,
        IAccessContext accessContext,
        Guid id,
        Func<ApplicationPermissions, bool> allowed,
        Func<ApplicationAccess, UserAccess, Task> action,
        CancellationToken ct)
    {
        var user = await accessContext.GetAsync(ct);
        var (access, denied) = await RecruitingAccess.LoadAsync(db, user, id, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!allowed(access!.Permissions))
        {
            return Results.Forbid();
        }

        await action(access, user);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await DetailAsync(db, user, access, ct));
    }

    private static async Task<ApplicationDetail> DetailAsync(HrPortalDbContext db, UserAccess user, ApplicationAccess access, CancellationToken ct)
    {
        var (application, requisition, permissions, _) = access;
        var insider = permissions.CanSeeAllFeedback;
        var person = await db.People.AsNoTracking().SingleAsync(p => p.Id == application.PersonId, ct);
        var profile = await db.CandidateProfiles.AsNoTracking().SingleOrDefaultAsync(c => c.PersonId == person.Id, ct);
        MoneyDto? Show(Money? money) => permissions.CanSeeCompensation && money is { } m ? new MoneyDto(m.Amount, m.Currency) : null;

        // Panelists who are not HR or the hiring manager see only the interviews they sit on.
        var interviews = await db.Interviews.AsNoTracking().Include(i => i.Panel)
            .Where(i => i.ApplicationId == application.Id)
            .OrderBy(i => i.Round).ThenBy(i => i.StartUtc)
            .ToListAsync(ct);
        if (!insider)
        {
            interviews = interviews.Where(i => user.EmployeeId is { } me && i.HasPanelist(me)).ToList();
        }

        var interviewIds = interviews.Select(i => i.Id).ToList();
        var scorecards = await db.Scorecards.AsNoTracking().Where(s => interviewIds.Contains(s.InterviewId)).ToListAsync(ct);
        var peopleIds = interviews.SelectMany(i => i.Panel.Select(p => p.EmployeeId)).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => peopleIds.Contains(e.Id))
            .Select(e => new { e.Id, Name = e.Person.GivenName + " " + e.Person.FamilyName })
            .ToDictionaryAsync(e => e.Id, e => e.Name, ct);

        var interviewDtos = interviews.Select(i =>
        {
            var all = scorecards.Where(s => s.InterviewId == i.Id).ToList();
            var visible = RecruitingRules.VisibleScorecards(i, all, user.EmployeeId, insider);
            var onPanel = user.EmployeeId is { } me && i.HasPanelist(me);
            var mine = onPanel && all.Any(s => s.InterviewerEmployeeId == user.EmployeeId);
            return new InterviewDto(
                i.Id, i.Round, i.StartUtc, i.DurationMinutes, i.Mode, i.Location, i.Status,
                i.Panel.Select(p => names[p.EmployeeId]).Order().ToList(), i.Criteria, all.Count, onPanel, mine,
                FeedbackHidden: onPanel && !mine && all.Count > 0,
                visible.Select(s => new ScorecardDto(names[s.InterviewerEmployeeId], s.Ratings, s.Recommendation, s.Comment, s.SubmittedAtUtc)).ToList());
        }).ToList();

        var notes = permissions.CanSeeNotes
            ? await db.ApplicationNotes.AsNoTracking().Where(n => n.ApplicationId == application.Id).OrderByDescending(n => n.CreatedAtUtc)
                .Select(n => new NoteDto(n.Id, n.Body, n.AuthorName, n.CreatedAtUtc)).ToListAsync(ct)
            : [];
        var history = insider
            ? application.History.OrderByDescending(h => h.AtUtc).ThenByDescending(h => h.Id)
                .Select(h => new StageChangeDto(h.FromStage, h.FromRound, h.ToStage, h.ToRound, h.RejectionReasonName, h.Reason, h.ActorName, h.AtUtc)).ToList()
            : [];
        var cvs = await db.CandidateCvs.AsNoTracking().Where(c => c.PersonId == person.Id).OrderByDescending(c => c.UploadedAtUtc)
            .Select(c => new CvDto(c.Id, c.FileName, c.SizeBytes, c.UploadedBy, c.UploadedAtUtc)).ToListAsync(ct);

        return new ApplicationDetail(
            application.Id, application.Stage, application.InterviewRound, application.Source, application.AppliedAtUtc,
            new CandidateSummary(person.Id, person.FullName, person.PersonalEmail, person.Phone, profile?.CurrentEmployer, profile?.NoticePeriodDays,
                Show(profile?.CurrentCtc), Show(profile?.ExpectedCtc)),
            new RequisitionRef(requisition.Id, requisition.Number, requisition.Title, requisition.Status, requisition.InterviewRounds, requisition.ScorecardCriteria),
            history, notes, interviewDtos, cvs, permissions);
    }
}
