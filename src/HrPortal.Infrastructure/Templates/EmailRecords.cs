using HrPortal.Domain.Common;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Templates;
using HrPortal.Infrastructure.Notifications;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Infrastructure.Templates;

/// <summary>
/// The record an email is sent from, resolved to its recipient and merge-field values (ET-02).
/// </summary>
/// <param name="ManagerEmail">The employee's manager, for templates that copy the manager.</param>
/// <param name="RequesterEmployeeId">For applications and requisitions: who raised the requisition (scoping).</param>
public sealed record EmailRecord(
    TemplateRecordType Type,
    Guid RecordId,
    Guid PersonId,
    Guid? EmployeeId,
    string RecipientName,
    string? RecipientEmail,
    string? ManagerEmail,
    string CountryCode,
    Guid? RequesterEmployeeId,
    Guid? HiringManagerEmployeeId,
    IReadOnlyDictionary<string, string?> Values);

public static class EmailRecords
{
    /// <summary>Loads the record, or null when it does not exist. <paramref name="senderName"/> fills {{sender_name}}.</summary>
    public static Task<EmailRecord?> LoadAsync(
        HrPortalDbContext db, TemplateRecordType type, Guid id, string senderName, PortalOptions portal, DateTimeOffset now, CancellationToken ct) =>
        type switch
        {
            TemplateRecordType.Application => ApplicationAsync(db, id, senderName, portal, now, ct),
            TemplateRecordType.Employee => EmployeeAsync(db, id, senderName, ct),
            TemplateRecordType.Requisition => RequisitionAsync(db, id, senderName, portal, ct),
            _ => Task.FromResult<EmailRecord?>(null),
        };

    private static async Task<EmailRecord?> ApplicationAsync(
        HrPortalDbContext db, Guid id, string senderName, PortalOptions portal, DateTimeOffset now, CancellationToken ct)
    {
        var x = await (
                from a in db.Applications.AsNoTracking()
                where a.Id == id
                join r in db.Requisitions on a.RequisitionId equals r.Id
                join p in db.People on a.PersonId equals p.Id
                join d in db.Departments on r.DepartmentId equals d.Id
                join o in db.OfficeLocations on r.OfficeLocationId equals o.Id
                join c in db.CountrySettings on r.CountryCode equals c.CountryCode
                join hm in db.Employees on r.HiringManagerEmployeeId equals hm.Id
                select new
                {
                    a.PersonId,
                    r.Number,
                    r.Title,
                    r.CountryCode,
                    r.RequesterEmployeeId,
                    r.HiringManagerEmployeeId,
                    p.GivenName,
                    p.FamilyName,
                    p.PersonalEmail,
                    Department = d.Name,
                    Office = o.Name,
                    TimeZone = o.TimeZone ?? c.DefaultTimeZone,
                    HiringManager = hm.Person.GivenName + " " + hm.Person.FamilyName,
                })
            .SingleOrDefaultAsync(ct);
        if (x is null)
        {
            return null;
        }

        // The next scheduled interview; failing that, the most recent one.
        var interviews = await db.Interviews.AsNoTracking().Include(i => i.Panel)
            .Where(i => i.ApplicationId == id && i.Status == InterviewStatus.Scheduled)
            .ToListAsync(ct);
        var interview = interviews.Where(i => i.StartUtc >= now).MinBy(i => i.StartUtc) ?? interviews.MaxBy(i => i.StartUtc);
        string? panel = null;
        if (interview is not null)
        {
            var ids = interview.Panel.Select(p => p.EmployeeId).ToList();
            var names = await db.Employees.Where(e => ids.Contains(e.Id))
                .OrderBy(e => e.Person.GivenName).Select(e => e.Person.GivenName + " " + e.Person.FamilyName).ToListAsync(ct);
            panel = string.Join(", ", names);
        }

        var joined = await db.Employees.Where(e => e.PersonId == x.PersonId).OrderByDescending(e => e.JoiningDate).Select(e => (DateOnly?)e.JoiningDate).FirstOrDefaultAsync(ct);
        var values = new Dictionary<string, string?>
        {
            ["first_name"] = x.GivenName,
            ["last_name"] = x.FamilyName,
            ["full_name"] = $"{x.GivenName} {x.FamilyName}",
            ["designation"] = x.Title,
            ["department"] = x.Department,
            ["office"] = x.Office,
            ["company_name"] = await CompanyNameAsync(db, portal, x.CountryCode, ct),
            ["sender_name"] = senderName,
            ["requisition_number"] = x.Number,
            ["requisition_title"] = x.Title,
            ["hiring_manager_name"] = x.HiringManager,
            ["manager_name"] = x.HiringManager,
            ["interview_date"] = interview is null ? null : LocalCalendar.FormatDate(interview.StartUtc, x.TimeZone),
            ["interview_time"] = interview is null ? null : LocalCalendar.FormatTime(interview.StartUtc, x.TimeZone),
            ["interview_round"] = interview?.Round.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["interview_mode"] = interview is null ? null : ModeLabel(interview.Mode),
            ["interview_location"] = interview?.Location,
            ["panel_names"] = panel,
            ["joining_date"] = joined is { } date ? LocalCalendar.FormatDate(date) : null,
        };
        return new EmailRecord(
            TemplateRecordType.Application, id, x.PersonId, null, $"{x.GivenName} {x.FamilyName}", x.PersonalEmail, null, x.CountryCode,
            x.RequesterEmployeeId, x.HiringManagerEmployeeId, values);
    }

    private static async Task<EmailRecord?> EmployeeAsync(HrPortalDbContext db, Guid id, string senderName, CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new
            {
                e.PersonId,
                e.Person.GivenName,
                e.Person.FamilyName,
                e.WorkEmail,
                e.EmployeeNumber,
                e.Designation,
                e.CountryCode,
                Department = e.Department.Name,
                Office = e.OfficeLocation.Name,
                Company = e.LegalEntity.Name,
                e.JoiningDate,
                e.ProbationEndDate,
                e.LastWorkingDate,
                Manager = e.Manager == null ? null : e.Manager.Person.GivenName + " " + e.Manager.Person.FamilyName,
                ManagerEmail = e.Manager == null ? null : e.Manager.WorkEmail,
            })
            .SingleOrDefaultAsync(ct);
        if (e is null)
        {
            return null;
        }

        var values = new Dictionary<string, string?>
        {
            ["first_name"] = e.GivenName,
            ["last_name"] = e.FamilyName,
            ["full_name"] = $"{e.GivenName} {e.FamilyName}",
            ["designation"] = e.Designation,
            ["department"] = e.Department,
            ["office"] = e.Office,
            ["company_name"] = e.Company,
            ["sender_name"] = senderName,
            ["employee_number"] = e.EmployeeNumber,
            ["joining_date"] = LocalCalendar.FormatDate(e.JoiningDate),
            ["manager_name"] = e.Manager,
            ["probation_end_date"] = e.ProbationEndDate is { } probation ? LocalCalendar.FormatDate(probation) : null,
            ["last_working_date"] = e.LastWorkingDate is { } last ? LocalCalendar.FormatDate(last) : null,
        };
        return new EmailRecord(
            TemplateRecordType.Employee, id, e.PersonId, id, $"{e.GivenName} {e.FamilyName}", e.WorkEmail, e.ManagerEmail, e.CountryCode, null, null, values);
    }

    /// <summary>Requisition emails go to the approver of the current step.</summary>
    private static async Task<EmailRecord?> RequisitionAsync(HrPortalDbContext db, Guid id, string senderName, PortalOptions portal, CancellationToken ct)
    {
        var r = await (
                from q in db.Requisitions.AsNoTracking()
                where q.Id == id
                join d in db.Departments on q.DepartmentId equals d.Id
                join o in db.OfficeLocations on q.OfficeLocationId equals o.Id
                join requester in db.Employees on q.RequesterEmployeeId equals requester.Id
                select new
                {
                    q.Number,
                    q.Title,
                    q.CountryCode,
                    q.Openings,
                    q.TargetDate,
                    q.ApprovalRound,
                    q.RequesterEmployeeId,
                    q.HiringManagerEmployeeId,
                    Department = d.Name,
                    Office = o.Name,
                    Requester = requester.Person.GivenName + " " + requester.Person.FamilyName,
                })
            .SingleOrDefaultAsync(ct);
        if (r is null)
        {
            return null;
        }

        var approver = await (
                from a in db.RequisitionApprovals
                where a.RequisitionId == id && a.Round == r.ApprovalRound && a.Decision == ApprovalDecision.Pending
                orderby a.StepOrder
                join e in db.Employees on a.ApproverEmployeeId equals e.Id
                select new { e.Id, e.PersonId, e.Person.GivenName, e.Person.FamilyName, e.WorkEmail })
            .FirstOrDefaultAsync(ct);

        var values = new Dictionary<string, string?>
        {
            ["first_name"] = approver?.GivenName,
            ["last_name"] = approver?.FamilyName,
            ["full_name"] = approver is null ? null : $"{approver.GivenName} {approver.FamilyName}",
            ["designation"] = r.Title,
            ["department"] = r.Department,
            ["office"] = r.Office,
            ["company_name"] = await CompanyNameAsync(db, portal, r.CountryCode, ct),
            ["sender_name"] = senderName,
            ["requisition_number"] = r.Number,
            ["requisition_title"] = r.Title,
            ["requester_name"] = r.Requester,
            ["openings"] = r.Openings.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["target_date"] = LocalCalendar.FormatDate(r.TargetDate),
            ["requisition_link"] = string.IsNullOrWhiteSpace(portal.BaseUrl) ? null : $"{portal.BaseUrl.TrimEnd('/')}/requisitions/{id}",
            ["hiring_manager_name"] = await db.Employees.Where(e => e.Id == r.HiringManagerEmployeeId)
                .Select(e => e.Person.GivenName + " " + e.Person.FamilyName).SingleOrDefaultAsync(ct),
        };

        // No pending approval step: there is no one to send to, which blocks sending.
        return new EmailRecord(
            TemplateRecordType.Requisition, id, approver?.PersonId ?? Guid.Empty, approver?.Id,
            approver is null ? "(no pending approver)" : $"{approver.GivenName} {approver.FamilyName}", approver?.WorkEmail, null, r.CountryCode,
            r.RequesterEmployeeId, r.HiringManagerEmployeeId, values);
    }

    /// <summary>The configured company name, or the country's legal entity when it has exactly one.</summary>
    private static async Task<string?> CompanyNameAsync(HrPortalDbContext db, PortalOptions portal, string countryCode, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(portal.CompanyName))
        {
            return portal.CompanyName;
        }

        var entities = await db.LegalEntities.Where(l => l.CountryCode == countryCode).Select(l => l.Name).Take(2).ToListAsync(ct);
        return entities.Count == 1 ? entities[0] : null;
    }

    private static string ModeLabel(InterviewMode mode) => mode switch
    {
        InterviewMode.InPerson => "In person",
        InterviewMode.Teams => "Microsoft Teams",
        InterviewMode.Phone => "Phone",
        _ => mode.ToString(),
    };
}
