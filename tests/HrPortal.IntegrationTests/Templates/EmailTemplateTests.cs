using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;
using HrPortal.Domain.Templates;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Employees.EmployeeEndpoints;
using static HrPortal.Api.Recruiting.ApplicationEndpoints;
using static HrPortal.Api.Recruiting.CandidateEndpoints;
using static HrPortal.Api.Requisitions.RequisitionEndpoints;
using static HrPortal.Api.Templates.EmailEndpoints;
using static HrPortal.Api.Templates.EmailTemplateEndpoints;

namespace HrPortal.IntegrationTests.Templates;

/// <summary>ET-01..06, ET-08: templates, merge fields, preview, editing, test, bulk send and the email log, with scopes.</summary>
[Collection(ApiCollection.Name)]
public class EmailTemplateTests(HrPortalApiFactory factory)
{
    private const string Hr = "dev-pk-hrexecutive";
    private const string HrAdmin = "dev-pk-hradmin";

    private async Task<Guid> TemplateIdAsync(string starterKey) =>
        await factory.WithDbAsync(db => db.EmailTemplates.Where(t => t.StarterKey == starterKey).Select(t => t.Id).SingleAsync());

    private async Task<RequisitionDetail> OpenRequisitionAsync(string country = "PK", string role = "hiringmanager", bool approve = true)
    {
        var requester = $"dev-{country.ToLowerInvariant()}-{role}";
        var ids = await factory.WithDbAsync(async db => new
        {
            Department = await db.Departments.Where(d => d.Code == "ENG").Select(d => d.Id).SingleAsync(),
            Office = await db.OfficeLocations.Where(o => o.Name == (country == "PK" ? "Lahore Office" : "US Office")).Select(o => o.Id).SingleAsync(),
        });
        var client = factory.CreateClientAs(requester);
        var created = await (await client.PostAsJsonAsync("/api/requisitions", new RequisitionRequest(
            "Data Engineer", ids.Department, ids.Office, 2, 1000, 2000, RequisitionReason.NewPosition, null, new DateOnly(2030, 1, 1), "Pipelines.")))
            .Content.ReadFromJsonAsync<RequisitionDetail>();
        var submitted = await client.PostAsJsonAsync($"/api/requisitions/{created!.Id}/submit", new { });
        if (!approve)
        {
            return (await submitted.Content.ReadFromJsonAsync<RequisitionDetail>())!;
        }

        var approved = await factory.CreateClientAs("dev-ceo").PostAsJsonAsync($"/api/requisitions/{created.Id}/decision", new DecisionRequest(true, null));
        return (await approved.Content.ReadFromJsonAsync<RequisitionDetail>())!;
    }

    private async Task<ApplicationCard> AddCandidateAsync(Guid requisitionId, string? email = "auto", string devUser = Hr)
    {
        var name = $"Sana{Guid.NewGuid():N}"[..12];
        var response = await factory.CreateClientAs(devUser).PostAsJsonAsync($"/api/requisitions/{requisitionId}/applications", new AddApplicationRequest(
            null,
            new CandidateRequest(name, "Iqbal", email == "auto" ? $"{name.ToLowerInvariant()}@mail.test" : email, email is null ? $"0300{Random.Shared.Next(1000000, 9999999)}" : null,
                "Acme", 30, null, null),
            CandidateSource.Referral));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApplicationCard>())!;
    }

    private async Task ScheduleAsync(Guid applicationId, DateTimeOffset start, params string[] panel)
    {
        var ids = await Task.WhenAll(panel.Select(factory.EmployeeIdAsync));
        var response = await factory.CreateClientAs(Hr).PostAsJsonAsync($"/api/applications/{applicationId}/interviews",
            new ScheduleInterviewRequest(1, start, 60, InterviewMode.Teams, "Teams link", ids));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> PostAsync(string devUser, string path, object body) => await factory.CreateClientAs(devUser).PostAsJsonAsync(path, body);

    private async Task<PreviewResponse> PreviewAsync(Guid templateId, Guid recordId, string devUser = Hr, string? subject = null, string? body = null)
    {
        var response = await PostAsync(devUser, "/api/emails/preview", new ComposeRequest(templateId, recordId, subject, body));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PreviewResponse>())!;
    }

    private static TemplateRequest NewTemplate(string body = "<p>Hi {{first_name}}</p>", TemplateCategory category = TemplateCategory.General) =>
        new($"Custom {Guid.NewGuid():N}"[..20], category, TemplateRecordType.Application, "About {{designation}}", body, null, SenderMailbox.CurrentUser, false, true);

    [Fact]
    public async Task ET_starter_templates_are_seeded_and_listed_per_role()
    {
        async Task<List<TemplateSummary>> ListAsync(string user) =>
            (await factory.CreateClientAs(user).GetFromJsonAsync<List<TemplateSummary>>("/api/email-templates"))!;

        var admin = await ListAsync(HrAdmin);
        var executive = await ListAsync(Hr);
        var manager = await ListAsync("dev-pk-hiringmanager");

        foreach (var starter in StarterTemplates.All)
        {
            Assert.Contains(admin, t => t.Name == starter.Content.Name && t.Trigger == starter.Trigger);
        }

        Assert.Contains(admin, t => t.Name == "Termination communication" && t.IsRestricted);
        Assert.DoesNotContain(executive, t => t.IsRestricted);
        Assert.All(manager, t => Assert.Equal(TemplateCategory.Interview, t.Category));
        Assert.Contains(manager, t => t.Name == "Interview confirmation");
        foreach (var outsider in new[] { "dev-pk-linemanager", "dev-pk-interviewer", "dev-pk-employee", "dev-pk-systemadmin" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs(outsider).GetAsync("/api/email-templates")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(outsider, "/api/emails/preview", new ComposeRequest(Guid.NewGuid(), Guid.NewGuid(), null, null))).StatusCode);
        }
    }

    [Fact]
    public async Task ET_01_06_HR_admins_edit_templates_with_sanitised_html_and_every_change_is_versioned_and_audited()
    {
        var created = await PostAsync(HrAdmin, "/api/email-templates", NewTemplate("<p onclick=\"x()\">Hi {{first_name}}<script>alert(1)</script></p>"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var template = (await created.Content.ReadFromJsonAsync<TemplateDetail>())!;
        Assert.Equal(("<p>Hi {{first_name}}</p>", 1), (template.BodyHtml, template.Version));

        var update = NewTemplate("<p>Hello {{first_name}}, about {{interview_date}}</p>") with { Name = template.Name, IsActive = false };
        var updated = await factory.CreateClientAs(HrAdmin).PutAsJsonAsync($"/api/email-templates/{template.Id}", update);
        var detail = (await updated.Content.ReadFromJsonAsync<TemplateDetail>())!;
        Assert.Equal((2, false), (detail.Version, detail.IsActive));
        Assert.Equal([2, 1], detail.Versions.Select(v => v.Version));
        var first = await factory.CreateClientAs(HrAdmin).GetFromJsonAsync<VersionDetail>($"/api/email-templates/{template.Id}/versions/1");
        Assert.Equal(("<p>Hi {{first_name}}</p>", true), (first!.BodyHtml, first.IsActive));
        Assert.True(await factory.WithDbAsync(db => db.AuditEntries.AnyAsync(a => a.EntityType == nameof(EmailTemplate) && a.EntityId == template.Id.ToString() && a.ActorId == HrAdmin)));

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(HrAdmin, "/api/email-templates", NewTemplate("<p>{{probation_end_date}}</p>"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(HrAdmin, "/api/email-templates", NewTemplate() with { Name = template.Name })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(Hr, "/api/email-templates", NewTemplate())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs(Hr).PutAsJsonAsync($"/api/email-templates/{template.Id}", update)).StatusCode);

        // MC-03: an HR Admin limited to Pakistan edits Pakistan templates only, not shared or US ones.
        await using var _ = await factory.SetHrAccessAsync(HrAdmin, CountryScope.Of(["PK"]));
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs(HrAdmin).PutAsJsonAsync($"/api/email-templates/{template.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(HrAdmin, "/api/email-templates", NewTemplate() with { CountryCode = "PK" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(HrAdmin, "/api/email-templates", NewTemplate() with { CountryCode = "US" })).StatusCode);
    }

    [Fact]
    public async Task ET_02_03_preview_fills_real_data_with_interview_time_in_the_office_zone_and_missing_fields_block_sending()
    {
        var requisition = await OpenRequisitionAsync();
        var scheduled = await AddCandidateAsync(requisition.Id);
        var unscheduled = await AddCandidateAsync(requisition.Id);
        await ScheduleAsync(scheduled.Id, new DateTimeOffset(2030, 11, 4, 10, 0, 0, TimeSpan.Zero), "PK-0007", "PK-0004");
        var confirmation = await TemplateIdAsync("interview-confirmation");

        var preview = await PreviewAsync(confirmation, scheduled.Id);

        Assert.True(preview.CanSend);
        Assert.Equal(scheduled.Email, preview.To);
        Assert.Equal("Your interview for Data Engineer on Mon 4 Nov 2030", preview.Subject);
        Assert.Contains($"Dear {scheduled.CandidateName.Split(' ')[0]},", preview.BodyHtml);
        Assert.Contains("15:00 Asia/Karachi (UTC+05:00)", preview.BodyHtml);
        Assert.Contains("Dev HiringManager (PK), Dev Interviewer (PK)", preview.BodyHtml);
        Assert.Contains("HR Portal Test Co", preview.BodyHtml);

        var blocked = await PreviewAsync(confirmation, unscheduled.Id);
        Assert.False(blocked.CanSend);
        Assert.Equal(["interview_date", "interview_time", "interview_mode", "interview_location", "panel_names"], blocked.MissingFields.Select(f => f.Key));
        var send = await PostAsync(Hr, "/api/emails/send", new SendRequest(confirmation, unscheduled.Id, null, null, SenderMailbox.HrMailbox, null));
        Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
        Assert.Contains("Interview date", await send.Content.ReadAsStringAsync());
        Assert.Empty(factory.Emails.To(unscheduled.Email!));
    }

    [Fact]
    public async Task ET_04_08_HR_edit_the_filled_email_before_sending_and_it_is_logged_on_the_candidate()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddCandidateAsync(requisition.Id);
        var rejection = await TemplateIdAsync("rejection");
        var preview = await PreviewAsync(rejection, card.Id);
        var editedBody = preview.BodyHtml.Replace("We wish you every success.", "<em>Thank you again.</em><script>x()</script>", StringComparison.Ordinal);

        var leftover = await PostAsync(Hr, "/api/emails/send", new SendRequest(rejection, card.Id, preview.Subject, editedBody + "<p>{{interview_date}}</p>", SenderMailbox.HrMailbox, null));
        var sent = await PostAsync(Hr, "/api/emails/send", new SendRequest(rejection, card.Id, "Update on your application", editedBody, SenderMailbox.HrMailbox, null));

        Assert.Equal(HttpStatusCode.BadRequest, leftover.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var message = Assert.Single(factory.Emails.To(card.Email!));
        Assert.Equal("Update on your application", message.Subject);
        Assert.Contains("<em>Thank you again.</em>", message.HtmlBody);
        Assert.DoesNotContain("script", message.HtmlBody);
        Assert.Equal("hr@hr.test", message.From!.Address);

        var log = (await sent.Content.ReadFromJsonAsync<EmailLogDto>())!;
        Assert.Equal((EmailStatus.Sent, "Rejection notification", 1, "Dev HrExecutive (PK)"), (log.Status, log.TemplateName, log.TemplateVersion!.Value, log.SentByName));
        var history = await factory.CreateClientAs(Hr).GetFromJsonAsync<List<EmailLogDto>>($"/api/people/{card.PersonId}/emails");
        Assert.Equal([log.Id], history!.Select(h => h.Id));
        var resumeBank = await factory.CreateClientAs(Hr).GetFromJsonAsync<Api.Recruiting.ResumeBankEndpoints.ResumeBankDetail>($"/api/resume-bank/{card.PersonId}");
        Assert.Contains(resumeBank!.Timeline, t => t.Kind == "email" && t.Title == "Email: Update on your application");

        // ET-08 history is HR's, in their countries.
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs("dev-us-hrexecutive").GetAsync($"/api/people/{card.PersonId}/emails")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs("dev-pk-hiringmanager").GetAsync($"/api/people/{card.PersonId}/emails")).StatusCode);
    }

    [Fact]
    public async Task ET_03_send_test_goes_to_the_user_only_and_is_not_logged()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddCandidateAsync(requisition.Id);
        var me = await factory.WithDbAsync(db => db.Employees.Where(e => e.UserObjectId == Hr).Select(e => e.WorkEmail).SingleAsync());
        var before = factory.Emails.To(me).Count;

        var response = await PostAsync(Hr, "/api/emails/test", new SendRequest(await TemplateIdAsync("interview-confirmation"), card.Id, null, null, SenderMailbox.CurrentUser, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(me, (await response.Content.ReadFromJsonAsync<TestResponse>())!.SentTo);
        var test = factory.Emails.To(me).Skip(before).Single();
        Assert.StartsWith("[Test] Your interview for Data Engineer", test.Subject);
        Assert.Contains("could not be sent yet", test.HtmlBody);
        Assert.Equal(me, test.From!.Address);
        Assert.Empty(factory.Emails.To(card.Email!));
        Assert.False(await factory.WithDbAsync(db => db.EmailLog.AnyAsync(e => e.PersonId == card.PersonId)));
    }

    [Fact]
    public async Task ET_05_bulk_send_personalises_each_email_and_reports_what_was_sent_and_what_failed()
    {
        var requisition = await OpenRequisitionAsync();
        var a = await AddCandidateAsync(requisition.Id);
        var b = await AddCandidateAsync(requisition.Id);
        var noEmail = await AddCandidateAsync(requisition.Id, email: null);
        var bounces = await AddCandidateAsync(requisition.Id);
        factory.Emails.Bounce[bounces.Email!] = true;

        var response = await PostAsync(Hr, "/api/emails/bulk", new BulkRequest(
            await TemplateIdAsync("rejection"), [a.Id, b.Id, noEmail.Id, bounces.Id, Guid.NewGuid()], SenderMailbox.HrMailbox, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<HrPortal.Infrastructure.Templates.BulkResult>())!;
        Assert.Equal(new[] { a.Id, b.Id }.Order(), result.Sent.Select(s => s.RecordId).Order());
        Assert.Equal(3, result.Failed.Count);
        Assert.Contains(result.Failed, f => f.RecordId == noEmail.Id && f.Error!.Contains("no email address"));
        Assert.Contains(result.Failed, f => f.RecordId == bounces.Id && f.Error!.Contains("mail server"));
        Assert.Contains(result.Failed, f => f.Name == "(unknown)");
        Assert.Contains(a.CandidateName.Split(' ')[0], Assert.Single(factory.Emails.To(a.Email!)).HtmlBody);
        Assert.Contains(b.CandidateName.Split(' ')[0], Assert.Single(factory.Emails.To(b.Email!)).HtmlBody);

        var logs = await factory.WithDbAsync(db => db.EmailLog.Where(e => e.BatchId == result.BatchId).ToListAsync());
        Assert.Equal(3, logs.Count);
        Assert.Equal(EmailStatus.Failed, logs.Single(l => l.RecordId == bounces.Id).Status);
    }

    [Fact]
    public async Task Rule_8_termination_communication_is_HR_admin_only_one_employee_at_a_time()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        await PostAsync(HrAdmin, $"/api/employees/{employee.Id}/status", new StatusChangeRequest(EmploymentStatus.Terminated, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "Misconduct"));
        var termination = await TemplateIdAsync("termination");
        var request = new SendRequest(termination, employee.Id, null, null, SenderMailbox.HrMailbox, null);

        var asExecutive = await PostAsync(Hr, "/api/emails/send", request);
        var bulk = await PostAsync(HrAdmin, "/api/emails/bulk", new BulkRequest(termination, [employee.Id], SenderMailbox.HrMailbox, null));
        var asAdmin = await PostAsync(HrAdmin, "/api/emails/send", request);

        Assert.Equal(HttpStatusCode.Forbidden, asExecutive.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, bulk.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
        var message = Assert.Single(factory.Emails.To(employee.WorkEmail));
        Assert.Contains("30 Sep 2026", message.HtmlBody);
        Assert.Equal(employee.Id, (await factory.WithDbAsync(db => db.EmailLog.SingleAsync(e => e.EmployeeId == employee.Id))).RecordId);
    }

    [Fact]
    public async Task ET_hiring_managers_send_interview_emails_for_their_own_requisitions_only()
    {
        var own = await OpenRequisitionAsync();
        var other = await OpenRequisitionAsync(role: "linemanager");
        var mine = await AddCandidateAsync(own.Id);
        var notMine = await AddCandidateAsync(other.Id);
        await ScheduleAsync(mine.Id, new DateTimeOffset(2030, 11, 4, 10, 0, 0, TimeSpan.Zero), "PK-0007");
        await ScheduleAsync(notMine.Id, new DateTimeOffset(2030, 11, 4, 10, 0, 0, TimeSpan.Zero), "PK-0007");
        var reminder = await TemplateIdAsync("interview-reminder");

        Assert.Equal(HttpStatusCode.OK, (await PostAsync("dev-pk-hiringmanager", "/api/emails/send", new SendRequest(reminder, mine.Id, null, null, SenderMailbox.CurrentUser, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("dev-pk-hiringmanager", "/api/emails/send", new SendRequest(reminder, notMine.Id, null, null, SenderMailbox.CurrentUser, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("dev-pk-hiringmanager", "/api/emails/send", new SendRequest(await TemplateIdAsync("rejection"), mine.Id, null, null, SenderMailbox.HrMailbox, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("dev-us-hrexecutive", "/api/emails/preview", new ComposeRequest(reminder, mine.Id, null, null))).StatusCode);
        var managerEmail = await factory.WithDbAsync(db => db.Employees.Where(e => e.UserObjectId == "dev-pk-hiringmanager").Select(e => e.WorkEmail).SingleAsync());
        Assert.Equal(managerEmail, factory.Emails.To(mine.Email!).Single().From!.Address);
    }

    [Fact]
    public async Task ET_employee_emails_copy_the_manager_and_requisition_emails_go_to_the_pending_approver()
    {
        var manager = await factory.EmployeeIdAsync("PK-0005");
        var withManager = await factory.CreateEmployeeAsync("PK", manager);
        var withoutManager = await factory.CreateEmployeeAsync("PK");
        var completion = await TemplateIdAsync("probation-completion");

        var ok = await PreviewAsync(completion, withManager.Id);
        var blocked = await PreviewAsync(completion, withoutManager.Id);

        var managerEmail = await factory.WithDbAsync(db => db.Employees.Where(e => e.Id == manager).Select(e => e.WorkEmail).SingleAsync());
        Assert.Equal([managerEmail], ok.Cc);
        Assert.True(ok.CanSend);
        Assert.Contains(blocked.Problems, p => p.Contains("no manager"));

        var pending = await OpenRequisitionAsync(approve: false);
        var approval = await PreviewAsync(await TemplateIdAsync("requisition-approval"), pending.Id);
        Assert.Equal("dev-ceo@hrportal.test", approval.To);
        Assert.Contains($"href=\"https://hr.test/requisitions/{pending.Id}\"", approval.BodyHtml);
        var open = await OpenRequisitionAsync();
        Assert.Contains((await PreviewAsync(await TemplateIdAsync("requisition-approval"), open.Id)).Problems, p => p.Contains("no approval step"));
    }

    [Fact]
    public async Task ET_01_attachments_are_sent_and_listed_on_the_log_and_only_the_uploader_can_use_them()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddCandidateAsync(requisition.Id);
        var file = new ByteArrayContent("%PDF-1.7 offer"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var upload = await factory.CreateClientAs(Hr).PostAsync("/api/emails/attachments", new MultipartFormDataContent { { file, "file", "offer.pdf" } });
        var attachment = (await upload.Content.ReadFromJsonAsync<AttachmentDto>())!;
        var offer = await TemplateIdAsync("offer");

        var byOther = await PostAsync(HrAdmin, "/api/emails/send", new SendRequest(offer, card.Id, null, null, SenderMailbox.HrMailbox, [attachment.Id]));
        var sent = await PostAsync(Hr, "/api/emails/send", new SendRequest(offer, card.Id, null, null, SenderMailbox.HrMailbox, [attachment.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, byOther.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var message = Assert.Single(factory.Emails.To(card.Email!));
        Assert.Equal("offer.pdf", Assert.Single(message.Attachments!).FileName);
        Assert.Equal(["offer.pdf"], (await sent.Content.ReadFromJsonAsync<EmailLogDto>())!.Attachments);
    }
}
