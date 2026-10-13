using HrPortal.Api.Auth;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Notifications;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using HrPortal.Infrastructure.Requisitions;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Requisitions;

/// <summary>
/// Job requisitions (REQ-01..04). Managers raise and see requisitions for their own department; HR sees and manages
/// all within their countries; approvers see what they are asked to approve. Approvers and requesters are notified
/// in-app and by email at each step.
/// </summary>
public static class RequisitionEndpoints
{
    public sealed record RequisitionSummary(
        Guid Id,
        string Number,
        string Title,
        string DepartmentName,
        string OfficeLocationName,
        string CountryCode,
        int Openings,
        decimal BudgetMin,
        decimal BudgetMax,
        string CurrencyCode,
        RequisitionStatus Status,
        DateOnly TargetDate,
        string RequesterName,
        DateTimeOffset CreatedAtUtc);

    public sealed record ApprovalDto(
        int Round, int StepOrder, string Label, Guid ApproverEmployeeId, string ApproverName, ApprovalDecision Decision, string? Comment, DateTimeOffset? DecidedAtUtc);

    public sealed record StatusChangeDto(RequisitionStatus? FromStatus, RequisitionStatus ToStatus, string ActorName, DateTimeOffset AtUtc, string? Comment);

    public sealed record RequisitionDetail(
        Guid Id,
        string Number,
        string Title,
        Guid DepartmentId,
        string DepartmentName,
        Guid OfficeLocationId,
        string OfficeLocationName,
        string CountryCode,
        int Openings,
        decimal BudgetMin,
        decimal BudgetMax,
        string CurrencyCode,
        RequisitionReason Reason,
        Guid? ReplacedEmployeeId,
        string? ReplacedEmployeeName,
        DateOnly TargetDate,
        string JobDescription,
        RequisitionStatus Status,
        Guid RequesterEmployeeId,
        string RequesterName,
        DateTimeOffset CreatedAtUtc,
        IReadOnlyList<ApprovalDto> Approvals,
        IReadOnlyList<StatusChangeDto> History,
        RequisitionPermissions Permissions);

    /// <summary>The budget is in the office's country currency (MC-02); there is no currency field to get wrong.</summary>
    public sealed record RequisitionRequest(
        string Title,
        Guid DepartmentId,
        Guid OfficeLocationId,
        int Openings,
        decimal BudgetMin,
        decimal BudgetMax,
        RequisitionReason Reason,
        Guid? ReplacedEmployeeId,
        DateOnly TargetDate,
        string JobDescription);

    public sealed record DecisionRequest(bool Approve, string? Comment);

    public sealed record StatusRequest(RequisitionStatus Status, string? Comment);

    public sealed record CommentRequest(string? Comment);

    public static IEndpointRouteBuilder MapRequisitionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/requisitions").WithTags("Requisitions");
        var view = Policies.Module(HrModule.Requisitions, Capability.View);
        var manage = Policies.Module(HrModule.Requisitions, Capability.Create);

        group.MapGet("/", ListAsync).RequireAuthorization(view);

        // Approvers need not hold a requisitions role (the CEO is an ordinary employee), so the record decides.
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(Policies.Authenticated);
        group.MapPost("/{id:guid}/decision", DecideAsync).RequireAuthorization(Policies.Authenticated);

        group.MapPost("/", CreateAsync).RequireAuthorization(manage);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(manage);
        group.MapPost("/{id:guid}/submit", SubmitAsync).RequireAuthorization(manage);
        group.MapPost("/{id:guid}/withdraw", WithdrawAsync).RequireAuthorization(manage);
        group.MapPost("/{id:guid}/status", ChangeStatusAsync).RequireAuthorization(manage);

        app.MapGet("/api/approvals", ApprovalsAsync).WithTags("Requisitions").RequireAuthorization(Policies.Authenticated);
        return app;
    }

    private static async Task<List<RequisitionSummary>> ListAsync(
        RequisitionStatus[]? status, Guid? departmentId, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct)
    {
        var query = db.Requisitions.AsNoTracking().VisibleTo(await accessContext.GetAsync(ct));
        if (status is { Length: > 0 })
        {
            query = query.Where(r => status.Contains(r.Status));
        }

        if (departmentId is { } department)
        {
            query = query.Where(r => r.DepartmentId == department);
        }

        return await SummariesAsync(db, query, ct);
    }

    /// <summary>Requisitions waiting for the signed-in user's decision.</summary>
    private static async Task<List<RequisitionSummary>> ApprovalsAsync(HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct)
    {
        var me = (await accessContext.GetAsync(ct)).EmployeeId;
        if (me is null)
        {
            return [];
        }

        // The current step is the lowest pending step of the current round.
        var query = db.Requisitions.AsNoTracking().Where(r =>
            r.Status == RequisitionStatus.PendingApproval
            && r.Approvals
                .Where(a => a.Round == r.ApprovalRound && a.Decision == ApprovalDecision.Pending)
                .OrderBy(a => a.StepOrder)
                .Select(a => a.ApproverEmployeeId)
                .FirstOrDefault() == me);
        return await SummariesAsync(db, query, ct);
    }

    private static async Task<IResult> GetAsync(Guid id, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        if (await DenyAsync(db, access, id, ct) is { } denied)
        {
            return denied;
        }

        return Results.Ok(await DetailAsync(db, access, await LoadAsync(db, id, ct), ct));
    }

    private static async Task<IResult> CreateAsync(
        RequisitionRequest request, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, TimeProvider clock, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        if (access.EmployeeId is not { } requester)
        {
            return Results.Forbid();
        }

        var (details, country) = await DetailsAsync(db, request, ct);
        if (!RequisitionRules.CanCreate(access, details.Department.Id, country.CountryCode))
        {
            return Results.Forbid();
        }

        var requisition = Requisition.Create(await db.NextRequisitionNumberAsync(ct), details, country, requester, ActorOf(user, access), clock.GetUtcNow());
        db.Requisitions.Add(requisition);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/requisitions/{requisition.Id}", await DetailAsync(db, access, requisition, ct));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, RequisitionRequest request, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct) =>
        await ActAsync(db, accessContext, id, p => p.CanEdit, async (requisition, access) =>
        {
            var (details, country) = await DetailsAsync(db, request, ct);

            // Moving a requisition to another department or country must stay within what the user could create.
            if (!RequisitionRules.IsHrFor(access, country.CountryCode, Capability.Edit)
                && !RequisitionRules.CanCreate(access, details.Department.Id, country.CountryCode))
            {
                return Results.Forbid();
            }

            requisition.Update(details, country);
            return null;
        }, ct);

    private static async Task<IResult> SubmitAsync(
        Guid id, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, INotificationEmailQueue emails, TimeProvider clock, CancellationToken ct)
    {
        var notifications = new List<Notification>();
        var result = await ActAsync(db, accessContext, id, p => p.CanSubmit, async (requisition, access) =>
        {
            var chain = await db.ApprovalChains.AsNoTracking().SingleOrDefaultAsync(c => c.CountryCode == requisition.CountryCode, ct);
            var steps = chain?.Steps ?? [];
            var approverIds = steps.Select(s => s.ApproverEmployeeId).ToList();
            var inactive = await db.Employees.Where(e => approverIds.Contains(e.Id)
                    && e.Status != EmploymentStatus.Probation && e.Status != EmploymentStatus.Confirmed && e.Status != EmploymentStatus.OnNotice)
                .AnyAsync(ct);
            if (inactive)
            {
                throw new DomainValidationException("approvalChain", "An approver in this country's chain has left. Ask an HR Admin to update the approval chain.");
            }

            var now = clock.GetUtcNow();
            requisition.Submit(steps, ActorOf(user, access), DateOnly.FromDateTime(now.UtcDateTime), now);
            var first = requisition.CurrentStep!;
            notifications.Add(await ApprovalRequestAsync(db, requisition, first, now, ct));
            return null;
        }, ct, notifications);

        Dispatch(emails, notifications);
        return result;
    }

    private static async Task<IResult> WithdrawAsync(
        Guid id, CommentRequest? request, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        await ActAsync(db, accessContext, id, p => p.CanWithdraw, (requisition, access) =>
        {
            requisition.Withdraw(ActorOf(user, access), clock.GetUtcNow(), request?.Comment);
            return Task.FromResult<IResult?>(null);
        }, ct);

    private static async Task<IResult> ChangeStatusAsync(
        Guid id, StatusRequest request, HrPortalDbContext db, IAccessContext accessContext, ICurrentUser user, TimeProvider clock, CancellationToken ct) =>
        await ActAsync(db, accessContext, id, p => p.StatusOptions.Contains(request.Status), (requisition, access) =>
        {
            requisition.ChangeStatus(request.Status, ActorOf(user, access), clock.GetUtcNow(), request.Comment);
            return Task.FromResult<IResult?>(null);
        }, ct);

    /// <summary>REQ-02: the current step's approver decides; the requester is told, and the next approver if any.</summary>
    private static async Task<IResult> DecideAsync(
        Guid id,
        DecisionRequest request,
        HrPortalDbContext db,
        IAccessContext accessContext,
        ICurrentUser user,
        INotificationEmailQueue emails,
        TimeProvider clock,
        CancellationToken ct)
    {
        var notifications = new List<Notification>();
        var result = await ActAsync(db, accessContext, id, p => p.CanDecide, async (requisition, access) =>
        {
            var now = clock.GetUtcNow();
            var step = requisition.CurrentStep!;
            var outcome = requisition.Decide(access.EmployeeId!.Value, request.Approve, request.Comment, ActorOf(user, access), now);
            var what = $"{requisition.Number}: {requisition.Title}";
            var link = $"/requisitions/{requisition.Id}";

            var (title, body) = (outcome.Approved, outcome.Opened) switch
            {
                (false, _) => ($"Requisition rejected: {what}",
                    $"{user.DisplayName} ({step.Label}) rejected your requisition and it is back in draft.\nComment: {request.Comment}\nYou can change it and submit it again."),
                (true, true) => ($"Requisition approved: {what}",
                    $"{user.DisplayName} ({step.Label}) approved your requisition. It is now an open position." + Comment(request.Comment)),
                _ => ($"Requisition approved by {step.Label}: {what}",
                    $"{user.DisplayName} ({step.Label}) approved your requisition. It now waits for {outcome.NextStep!.Label}." + Comment(request.Comment)),
            };
            notifications.Add(new Notification(requisition.RequesterEmployeeId, title, body, link, now));

            if (outcome.NextStep is { } next)
            {
                notifications.Add(await ApprovalRequestAsync(db, requisition, next, now, ct));
            }

            return null;
        }, ct, notifications);

        Dispatch(emails, notifications);
        return result;
    }

    /// <summary>Loads the requisition, checks visibility and the permission, runs the action and saves.</summary>
    private static async Task<IResult> ActAsync(
        HrPortalDbContext db,
        IAccessContext accessContext,
        Guid id,
        Func<RequisitionPermissions, bool> allowed,
        Func<Requisition, UserAccess, Task<IResult?>> action,
        CancellationToken ct,
        List<Notification>? notifications = null)
    {
        var access = await accessContext.GetAsync(ct);
        if (await DenyAsync(db, access, id, ct) is { } denied)
        {
            return denied;
        }

        var requisition = await LoadAsync(db, id, ct, tracked: true);
        if (!allowed(RequisitionRules.For(access, requisition)))
        {
            return Results.Forbid();
        }

        if (await action(requisition, access) is { } early)
        {
            return early;
        }

        db.Notifications.AddRange(notifications ?? []);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await DetailAsync(db, access, requisition, ct));
    }

    private static async Task<Notification> ApprovalRequestAsync(
        HrPortalDbContext db, Requisition requisition, RequisitionApproval step, DateTimeOffset now, CancellationToken ct)
    {
        var names = await db.Employees.Where(e => e.Id == requisition.RequesterEmployeeId)
            .Select(e => e.Person.GivenName + " " + e.Person.FamilyName).SingleAsync(ct);
        var department = await db.Departments.Where(d => d.Id == requisition.DepartmentId).Select(d => d.Name).SingleAsync(ct);
        return new Notification(
            step.ApproverEmployeeId,
            $"Approval needed: {requisition.Number} {requisition.Title}",
            $"{names} requests {requisition.Openings} × {requisition.Title} in {department} ({requisition.CountryCode}), " +
            $"budget {requisition.CurrencyCode} {requisition.BudgetMin:N0} – {requisition.BudgetMax:N0}, target {requisition.TargetDate:d MMM yyyy}.\n" +
            $"You are asked to approve as {step.Label}.",
            $"/requisitions/{requisition.Id}",
            now);
    }

    private static void Dispatch(INotificationEmailQueue emails, List<Notification> notifications)
    {
        // Only after the change is saved, so an email never announces something that was rolled back.
        foreach (var notification in notifications)
        {
            emails.Enqueue(notification.Id);
        }
    }

    private static string Comment(string? comment) => string.IsNullOrWhiteSpace(comment) ? string.Empty : $"\nComment: {comment}";

    private static Actor ActorOf(ICurrentUser user, UserAccess access) => new(user.UserId, user.DisplayName, access.EmployeeId);

    private static async Task<IResult?> DenyAsync(HrPortalDbContext db, UserAccess access, Guid id, CancellationToken ct)
    {
        if (await db.Requisitions.VisibleTo(access).AnyAsync(r => r.Id == id, ct))
        {
            return null;
        }

        return await db.Requisitions.AnyAsync(r => r.Id == id, ct) ? Results.Forbid() : Results.NotFound();
    }

    private static async Task<Requisition> LoadAsync(HrPortalDbContext db, Guid id, CancellationToken ct, bool tracked = false)
    {
        var query = db.Requisitions.Include(r => r.Approvals).Include(r => r.StatusHistory).AsSplitQuery();
        return await (tracked ? query : query.AsNoTracking()).SingleAsync(r => r.Id == id, ct);
    }

    private static async Task<(RequisitionDetails Details, Domain.Organisation.CountrySettings Country)> DetailsAsync(
        HrPortalDbContext db, RequisitionRequest request, CancellationToken ct)
    {
        var department = await db.Departments.SingleOrDefaultAsync(d => d.Id == request.DepartmentId, ct)
            ?? throw new DomainValidationException("departmentId", "Choose a department.");
        var office = await db.OfficeLocations.SingleOrDefaultAsync(o => o.Id == request.OfficeLocationId, ct)
            ?? throw new DomainValidationException("officeLocationId", "Choose an office.");
        var country = await db.CountrySettings.SingleAsync(c => c.CountryCode == office.CountryCode, ct);
        var replaced = request.ReplacedEmployeeId is { } replacedId
            ? await db.Employees.SingleOrDefaultAsync(e => e.Id == replacedId, ct)
                ?? throw new DomainValidationException("replacedEmployeeId", "Choose an existing employee.")
            : null;

        return (new RequisitionDetails(
            request.Title,
            department,
            office,
            request.Openings,
            new Money(request.BudgetMin, country.CurrencyCode),
            new Money(request.BudgetMax, country.CurrencyCode),
            request.Reason,
            replaced,
            request.TargetDate,
            request.JobDescription), country);
    }

    private static async Task<List<RequisitionSummary>> SummariesAsync(HrPortalDbContext db, IQueryable<Requisition> query, CancellationToken ct) =>
        await (
                from r in query
                join d in db.Departments on r.DepartmentId equals d.Id
                join o in db.OfficeLocations on r.OfficeLocationId equals o.Id
                join e in db.Employees on r.RequesterEmployeeId equals e.Id
                orderby r.CreatedAtUtc descending
                select new RequisitionSummary(
                    r.Id, r.Number, r.Title, d.Name, o.Name, r.CountryCode, r.Openings, r.BudgetMin, r.BudgetMax, r.CurrencyCode,
                    r.Status, r.TargetDate, e.Person.GivenName + " " + e.Person.FamilyName, r.CreatedAtUtc))
            .ToListAsync(ct);

    private static async Task<RequisitionDetail> DetailAsync(HrPortalDbContext db, UserAccess access, Requisition r, CancellationToken ct)
    {
        var people = new[] { r.RequesterEmployeeId }
            .Concat(r.ReplacedEmployeeId is { } replaced ? [replaced] : [])
            .Concat(r.Approvals.Select(a => a.ApproverEmployeeId))
            .Distinct()
            .ToList();
        var names = await db.Employees.AsNoTracking().Where(e => people.Contains(e.Id))
            .Select(e => new { e.Id, Name = e.Person.GivenName + " " + e.Person.FamilyName })
            .ToDictionaryAsync(e => e.Id, e => e.Name, ct);
        var department = await db.Departments.Where(d => d.Id == r.DepartmentId).Select(d => d.Name).SingleAsync(ct);
        var office = await db.OfficeLocations.Where(o => o.Id == r.OfficeLocationId).Select(o => o.Name).SingleAsync(ct);

        return new RequisitionDetail(
            r.Id, r.Number, r.Title, r.DepartmentId, department, r.OfficeLocationId, office, r.CountryCode, r.Openings,
            r.BudgetMin, r.BudgetMax, r.CurrencyCode, r.Reason, r.ReplacedEmployeeId,
            r.ReplacedEmployeeId is { } replacedId ? names[replacedId] : null,
            r.TargetDate, r.JobDescription, r.Status, r.RequesterEmployeeId, names[r.RequesterEmployeeId], r.CreatedAtUtc,
            r.Approvals.OrderByDescending(a => a.Round).ThenBy(a => a.StepOrder)
                .Select(a => new ApprovalDto(a.Round, a.StepOrder, a.Label, a.ApproverEmployeeId, names[a.ApproverEmployeeId], a.Decision, a.Comment, a.DecidedAtUtc))
                .ToList(),
            r.StatusHistory.OrderByDescending(h => h.AtUtc).ThenByDescending(h => h.Id)
                .Select(h => new StatusChangeDto(h.FromStatus, h.ToStatus, h.ActorName, h.AtUtc, h.Comment))
                .ToList(),
            RequisitionRules.For(access, r));
    }
}
