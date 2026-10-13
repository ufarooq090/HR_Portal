using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.Infrastructure.Employees;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using static HrPortal.Api.Employees.EmployeeEndpoints;
using static HrPortal.Api.Recruiting.ApplicationEndpoints;
using static HrPortal.Api.Recruiting.CandidateEndpoints;
using static HrPortal.Api.Recruiting.ResumeBankEndpoints;
using static HrPortal.Api.Requisitions.RequisitionEndpoints;
using NoteRequest = HrPortal.Api.Recruiting.ResumeBankEndpoints.NoteRequest;
using PageSize = UglyToad.PdfPig.Content.PageSize;
using StatusRequest = HrPortal.Api.Recruiting.ResumeBankEndpoints.StatusRequest;

namespace HrPortal.IntegrationTests.Recruiting;

/// <summary>RB-01..06, RB-08, RB-12: the Resume Bank, its search, statuses, history, import and scopes.</summary>
[Collection(ApiCollection.Name)]
public class ResumeBankTests(HrPortalApiFactory factory)
{
    private const string Hr = "dev-pk-hrexecutive";
    private const string HrAdmin = "dev-pk-hradmin";

    private const string Header =
        "GivenName,FamilyName,Email,Phone,Country,Location,CurrentEmployer,ExperienceYears,NoticePeriodDays,Skills,Tags,CurrentCtc,ExpectedCtc,CtcCurrency,Status,LastContacted,Notes";

    /// <summary>A made-up word, so searches only match this test's data in the shared database.</summary>
    private static string Word() => new(Guid.NewGuid().ToString("N").Where(char.IsAsciiLetterLower).Take(6).Prepend('q').Append('x').ToArray());

    private static MultipartFormDataContent Upload(byte[] bytes, string name)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { content, "file", name } };
    }

    private static MultipartFormDataContent Csv(params string[] rows) => Upload(Encoding.UTF8.GetBytes(string.Join("\n", rows.Prepend(Header))), "candidates.csv");

    private static string Row(string given, string family, string email, string country = "PK", string location = "Lahore", string experience = "5",
        string notice = "30", string skills = "", string tags = "", string ctc = "", string currency = "", string status = "", string contacted = "", string notes = "") =>
        $"{given},{family},{email},,{country},{location},Acme,{experience},{notice},\"{skills}\",\"{tags}\",{ctc},,{currency},{status},{contacted},{notes}";

    private async Task<ImportResult> ImportAsync(MultipartFormDataContent form, string devUser = Hr)
    {
        var response = await factory.CreateClientAs(devUser).PostAsync("/api/resume-bank/import", form);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ImportResult>())!;
    }

    private async Task<SearchResult> SearchAsync(string query, string devUser = Hr)
    {
        var response = await factory.CreateClientAs(devUser).GetAsync($"/api/resume-bank?{query}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SearchResult>())!;
    }

    private async Task<ResumeBankDetail> DetailAsync(Guid personId, string devUser = Hr) =>
        (await factory.CreateClientAs(devUser).GetFromJsonAsync<ResumeBankDetail>($"/api/resume-bank/{personId}"))!;

    private async Task<HttpResponseMessage> PostAsync(string devUser, string path, object body) =>
        await factory.CreateClientAs(devUser).PostAsJsonAsync(path, body);

    /// <summary>Imports one candidate and returns their person id.</summary>
    private async Task<Guid> ImportOneAsync(string country = "PK", string skills = "", string status = "", string ctc = "", string notes = "")
    {
        var email = $"rb.{Guid.NewGuid():N}@mail.test";
        var result = await ImportAsync(Csv(Row("Rb", "Candidate", email, country, skills: skills, status: status, ctc: ctc, notes: notes)), country == "PK" ? Hr : "dev-us-hrexecutive");
        Assert.Equal(1, result.Imported);
        return await factory.WithDbAsync(db => db.People.Where(p => p.PersonalEmail == email).Select(p => p.Id).SingleAsync());
    }

    private async Task<RequisitionDetail> OpenRequisitionAsync(string country = "PK")
    {
        var requester = $"dev-{country.ToLowerInvariant()}-hiringmanager";
        var ids = await factory.WithDbAsync(async db => new
        {
            Department = await db.Departments.Where(d => d.Code == "ENG").Select(d => d.Id).SingleAsync(),
            Office = await db.OfficeLocations.Where(o => o.Name == (country == "PK" ? "Lahore Office" : "US Office")).Select(o => o.Id).SingleAsync(),
        });
        var client = factory.CreateClientAs(requester);
        var created = await (await client.PostAsJsonAsync("/api/requisitions", new RequisitionRequest(
            "Data Engineer", ids.Department, ids.Office, 2, 1000, 2000, RequisitionReason.NewPosition, null, new DateOnly(2030, 1, 1), "Pipelines.")))
            .Content.ReadFromJsonAsync<RequisitionDetail>();
        await client.PostAsJsonAsync($"/api/requisitions/{created!.Id}/submit", new { });
        var approved = await factory.CreateClientAs("dev-ceo").PostAsJsonAsync($"/api/requisitions/{created.Id}/decision", new DecisionRequest(true, null));
        return (await approved.Content.ReadFromJsonAsync<RequisitionDetail>())!;
    }

    /// <summary>"Add to requisition" in the Resume Bank is the Candidate Tracker's add, with the existing person.</summary>
    private async Task<HttpResponseMessage> AddToRequisitionAsync(Guid requisitionId, Guid personId, string devUser = Hr) =>
        await PostAsync(devUser, $"/api/requisitions/{requisitionId}/applications", new AddApplicationRequest(personId, null, CandidateSource.Referral));

    private static byte[] Pdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.AddText(text, 11, new PdfPoint(50, 780), builder.AddStandard14Font(Standard14Font.Helvetica));
        return builder.Build();
    }

    private static byte[] Docx(string text)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            document.AddMainDocumentPart().Document = new Document(new Body(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }))));
        }

        return stream.ToArray();
    }

    [Fact]
    public async Task RB_04_full_text_search_finds_words_inside_PDF_and_Word_CVs_with_stemming()
    {
        var (pdfWord, docxWord) = (Word(), Word());
        var (a, b) = (await ImportOneAsync(), await ImportOneAsync());

        var pdf = await factory.CreateClientAs(Hr).PostAsync($"/api/candidates/{a}/cvs", Upload(Pdf($"Migrated {pdfWord} clusters to PostgreSQL"), "a.pdf"));
        var docx = await factory.CreateClientAs(Hr).PostAsync($"/api/candidates/{b}/cvs", Upload(Docx($"Led the {docxWord} platform team"), "b.docx"));
        Assert.True(pdf.IsSuccessStatusCode, await pdf.Content.ReadAsStringAsync());
        Assert.True(docx.IsSuccessStatusCode, await docx.Content.ReadAsStringAsync());

        Assert.Equal([a], (await SearchAsync($"q={pdfWord}")).Items.Select(i => i.PersonId));
        Assert.Equal([a], (await SearchAsync($"q={pdfWord}+migrating")).Items.Select(i => i.PersonId));
        Assert.Empty((await SearchAsync($"q={pdfWord}+-postgresql")).Items);
        Assert.Equal([b], (await SearchAsync($"q={docxWord}")).Items.Select(i => i.PersonId));
        Assert.Equal(new[] { a, b }.Order(), (await SearchAsync($"q={pdfWord}+or+{docxWord}")).Items.Select(i => i.PersonId).Order());
        Assert.True((await SearchAsync($"q={pdfWord}")).Items.Single().HasCv);
    }

    [Fact]
    public async Task RB_04_filters_on_skills_experience_location_notice_status_contact_date_and_salary()
    {
        var skill = Word();
        var result = await ImportAsync(Csv(
            Row("Filter", "Senior", $"{skill}.1@mail.test", experience: "8", notice: "60", skills: $"{skill}; SQL", ctc: "5000000", status: "Shortlisted", contacted: "2026-09-01", tags: "Future data roles"),
            Row("Filter", "Junior", $"{skill}.2@mail.test", location: "Karachi", experience: "2", notice: "15", skills: $"{skill}", ctc: "1500000", status: "Not shortlisted", contacted: "2025-01-10")));
        Assert.Equal(2, result.Imported);

        async Task<string[]> Names(string filter) => (await SearchAsync($"skills={skill}&{filter}")).Items.Select(i => i.Name).Order().ToArray();

        Assert.Equal(["Filter Junior", "Filter Senior"], await Names(""));
        Assert.Equal(["Filter Senior"], await Names("skills=sql"));
        Assert.Equal(["Filter Senior"], await Names("minExperience=5"));
        Assert.Equal(["Filter Junior"], await Names("maxExperience=3"));
        Assert.Equal(["Filter Junior"], await Names("location=karach"));
        Assert.Equal(["Filter Junior"], await Names("maxNoticeDays=30"));
        Assert.Equal(["Filter Junior"], await Names("status=NotShortlisted"));
        Assert.Equal(["Filter Senior"], await Names("contactedSince=2026-01-01"));
        Assert.Equal(["Filter Senior"], await Names("tag=future+data+roles"));
        Assert.Equal(["Filter Junior"], await Names("maxCurrentCtc=2000000&ctcCurrency=PKR"));
        Assert.Empty(await Names("maxCurrentCtc=2000000&ctcCurrency=USD"));
        Assert.Equal(new MoneyDto(5_000_000, "PKR"), (await SearchAsync($"skills={skill}&minExperience=5")).Items.Single().CurrentCtc);

        var paged = await SearchAsync($"skills={skill}&pageSize=1&page=2");
        Assert.Equal((2, 1), (paged.Total, paged.Items.Count));
    }

    [Fact]
    public async Task RB_scope_HR_see_their_countries_and_hiring_managers_see_CVs_and_outcomes_without_contact_salary_or_notes()
    {
        var skill = Word();
        var pk = await ImportOneAsync("PK", skill, ctc: "3000000", notes: "Prefers remote work");
        var us = await ImportOneAsync("US", skill, ctc: "90000");
        await factory.CreateClientAs(Hr).PostAsync($"/api/candidates/{pk}/cvs", Upload(Pdf("Python developer"), "cv.pdf"));

        Assert.Equal([pk], (await SearchAsync($"skills={skill}")).Items.Select(i => i.PersonId));
        Assert.Equal([us], (await SearchAsync($"skills={skill}", "dev-us-hrexecutive")).Items.Select(i => i.PersonId));
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs(Hr).GetAsync($"/api/resume-bank/{us}")).StatusCode);
        Assert.Equal(2, (await SearchAsync($"skills={skill}", HrAdmin)).Total);

        var managerRows = await SearchAsync($"skills={skill}", "dev-us-hiringmanager");
        Assert.Equal(2, managerRows.Total);
        Assert.All(managerRows.Items, r => Assert.Null(r.CurrentCtc));
        Assert.Empty((await SearchAsync($"skills={skill}&maxCurrentCtc=99999999&ctcCurrency=PKR", "dev-us-hiringmanager")).Items);

        var asManager = await DetailAsync(pk, "dev-us-hiringmanager");
        Assert.Equal((null, null, null, null), (asManager.Email, asManager.Phone, asManager.CurrentCtc, asManager.BlacklistReason));
        Assert.Single(asManager.Cvs);
        Assert.DoesNotContain(asManager.Timeline, t => t.Kind == "note");
        Assert.False(asManager.Permissions.CanEdit);
        var cvDownload = await factory.CreateClientAs("dev-us-hiringmanager").GetAsync($"/api/candidates/{pk}/cvs/{asManager.Cvs[0].Id}/content");
        Assert.Equal(HttpStatusCode.OK, cvDownload.StatusCode);

        var asHr = await DetailAsync(pk);
        Assert.NotNull(asHr.Email);
        Assert.Equal(new MoneyDto(3_000_000, "PKR"), asHr.CurrentCtc);
        Assert.Contains(asHr.Timeline, t => t.Kind == "note" && t.Detail == "Prefers remote work");

        foreach (var outsider in new[] { "dev-pk-linemanager", "dev-pk-interviewer", "dev-pk-employee" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs(outsider).GetAsync("/api/resume-bank")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs(outsider).GetAsync($"/api/resume-bank/{pk}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("dev-us-hiringmanager", $"/api/resume-bank/{pk}/notes", new NoteRequest("Hi"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs("dev-us-hiringmanager").PostAsync("/api/resume-bank/import", Csv())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("dev-us-hrexecutive", $"/api/resume-bank/{pk}/status", new StatusRequest(ResumeBankStatus.Hired, null))).StatusCode);
    }

    [Fact]
    public async Task RB_scope_hiring_managers_see_salary_only_for_candidates_on_their_own_requisitions_and_it_is_audited()
    {
        var person = await ImportOneAsync("PK", ctc: "2500000");
        var requisition = await OpenRequisitionAsync();
        Assert.Equal(HttpStatusCode.Created, (await AddToRequisitionAsync(requisition.Id, person)).StatusCode);

        var own = await DetailAsync(person, "dev-pk-hiringmanager");
        var other = await DetailAsync(person, "dev-us-hiringmanager");

        Assert.Equal(new MoneyDto(2_500_000, "PKR"), own.CurrentCtc);
        Assert.True(own.Applications.Single().CanOpen);
        Assert.Null(other.CurrentCtc);
        Assert.False(other.Applications.Single().CanOpen);
        Assert.True(await factory.WithDbAsync(db => db.AuditEntries.AnyAsync(a =>
            a.Action == AuditAction.Viewed && a.EntityType == CompensationAuditType && a.EntityId == person.ToString() && a.ActorId == "dev-pk-hiringmanager")));
        Assert.False(await factory.WithDbAsync(db => db.AuditEntries.AnyAsync(a =>
            a.Action == AuditAction.Viewed && a.EntityType == CompensationAuditType && a.EntityId == person.ToString() && a.ActorId == "dev-us-hiringmanager")));
    }

    [Fact]
    public async Task RB_03_blacklisting_is_HR_admin_only_needs_a_reason_is_audited_and_blocks_new_applications()
    {
        var person = await ImportOneAsync();
        var requisition = await OpenRequisitionAsync();

        var asExecutive = await PostAsync(Hr, $"/api/resume-bank/{person}/blacklist", new ReasonRequest("Forged letters"));
        var noReason = await PostAsync(HrAdmin, $"/api/resume-bank/{person}/blacklist", new ReasonRequest(" "));
        var blacklisted = await PostAsync(HrAdmin, $"/api/resume-bank/{person}/blacklist", new ReasonRequest("Forged letters"));
        var asPlainStatus = await PostAsync(Hr, $"/api/resume-bank/{person}/status", new StatusRequest(ResumeBankStatus.Blacklisted, null));

        Assert.Equal(HttpStatusCode.Forbidden, asExecutive.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.OK, blacklisted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, asPlainStatus.StatusCode);
        var detail = (await blacklisted.Content.ReadFromJsonAsync<ResumeBankDetail>())!;
        Assert.Equal((ResumeBankStatus.Blacklisted, "Forged letters"), (detail.Status, detail.BlacklistReason));
        var audited = await factory.WithDbAsync(db => db.AuditEntries
            .Where(a => a.EntityType == nameof(CandidateProfile) && a.EntityId == person.ToString() && a.ActorId == HrAdmin)
            .Select(a => a.NewValues).ToListAsync());
        Assert.Contains(audited, v => v != null && v.Contains("Blacklisted", StringComparison.Ordinal) && v.Contains("Forged letters", StringComparison.Ordinal));
        Assert.Null((await DetailAsync(person, "dev-us-hiringmanager")).BlacklistReason);

        var add = await AddToRequisitionAsync(requisition.Id, person);
        Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);
        Assert.Contains("blacklisted", await add.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var removed = await PostAsync(HrAdmin, $"/api/resume-bank/{person}/unblacklist", new ReasonRequest("Letters verified"));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Null((await removed.Content.ReadFromJsonAsync<ResumeBankDetail>())!.Status);
        Assert.Equal(HttpStatusCode.Created, (await AddToRequisitionAsync(requisition.Id, person)).StatusCode);
    }

    [Fact]
    public async Task RB_03_status_follows_the_candidate_tracker_and_is_shown_in_the_timeline()
    {
        var person = await ImportOneAsync();
        var requisition = await OpenRequisitionAsync();
        var added = await AddToRequisitionAsync(requisition.Id, person);
        var card = (await added.Content.ReadFromJsonAsync<ApplicationCard>())!;

        await PostAsync(Hr, $"/api/applications/{card.Id}/stage", new StageRequest(PipelineStage.Screening, null, null, null));
        Assert.Equal(ResumeBankStatus.Shortlisted, (await DetailAsync(person)).Status);
        await PostAsync(Hr, $"/api/applications/{card.Id}/stage", new StageRequest(PipelineStage.Rejected, null, new Guid("0199c3a0-0000-7000-8000-000000000001"), "Weak SQL"));

        var detail = await DetailAsync(person);
        Assert.Equal(ResumeBankStatus.NotShortlisted, detail.Status);
        Assert.NotNull(detail.LastContactedAtUtc);
        Assert.Equal(PipelineStage.Rejected, detail.Applications.Single().Stage);
        Assert.Contains(detail.Timeline, t => t.Kind == "applied" && t.ApplicationId == card.Id);
        Assert.Contains(detail.Timeline, t => t.Kind == "stage" && t.Detail == "Skills do not match the role: Weak SQL");
        Assert.Contains(detail.Timeline, t => t.Kind == "status" && t.Title.EndsWith("NotShortlisted", StringComparison.Ordinal));

        // Hiring managers of other requisitions see the outcome but not the free-text reason.
        Assert.Contains((await DetailAsync(person, "dev-us-hiringmanager")).Timeline, t => t.Kind == "stage" && t.Detail == "Skills do not match the role");
    }

    [Fact]
    public async Task RB_03_employees_who_leave_get_resigned_or_terminated_status()
    {
        var resigning = await factory.CreateEmployeeAsync("PK");
        var terminated = await factory.CreateEmployeeAsync("PK");

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(HrAdmin, $"/api/employees/{resigning.Id}/status", new StatusChangeRequest(EmploymentStatus.Resigned, new DateOnly(2026, 9, 1), null, "Moving abroad"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(HrAdmin, $"/api/employees/{terminated.Id}/status", new StatusChangeRequest(EmploymentStatus.Terminated, new DateOnly(2026, 9, 1), null, "Misconduct"))).StatusCode);

        var statuses = await factory.WithDbAsync(async db => await (
            from p in db.CandidateProfiles
            join e in db.Employees on p.PersonId equals e.PersonId
            where e.Id == resigning.Id || e.Id == terminated.Id
            select new { e.Id, p.Status, p.CountryCode }).ToListAsync());
        Assert.Equal((ResumeBankStatus.Resigned, "PK"), statuses.Where(s => s.Id == resigning.Id).Select(s => (s.Status!.Value, s.CountryCode)).Single());
        Assert.Equal(ResumeBankStatus.Terminated, statuses.Single(s => s.Id == terminated.Id).Status);
    }

    [Fact]
    public async Task RB_05_06_HR_edit_skills_tags_status_and_notes_and_record_contact()
    {
        var person = await ImportOneAsync();

        var details = await factory.CreateClientAs(Hr).PutAsJsonAsync($"/api/resume-bank/{person}/details", new DetailsRequest("PK", "Islamabad", 6.5m, ["Spark", "SQL"], ["Future data roles"]));
        var toUs = await factory.CreateClientAs(Hr).PutAsJsonAsync($"/api/resume-bank/{person}/details", new DetailsRequest("US", null, null, [], []));
        var status = await PostAsync(Hr, $"/api/resume-bank/{person}/status", new StatusRequest(ResumeBankStatus.Shortlisted, "Good fit for data roles"));
        var note = await PostAsync(Hr, $"/api/resume-bank/{person}/notes", new NoteRequest("Called; open to offers in Q1."));
        var contacted = await PostAsync(Hr, $"/api/resume-bank/{person}/contacted", new { });

        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, toUs.StatusCode);
        var detail = (await contacted.Content.ReadFromJsonAsync<ResumeBankDetail>())!;
        Assert.Equal(("Islamabad", 6.5m, ResumeBankStatus.Shortlisted), (detail.Location, detail.ExperienceYears!.Value, detail.Status!.Value));
        Assert.Equal(["spark", "sql"], detail.Skills);
        Assert.Equal(["future data roles"], detail.Tags);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(HttpStatusCode.OK, note.StatusCode);
        Assert.Contains(detail.Timeline, t => t.Kind == "note" && t.Detail == "Called; open to offers in Q1.");
        Assert.Contains(detail.Timeline, t => t.Kind == "status" && t.Detail == "Good fit for data roles");
        Assert.True(detail.LastContactedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task RB_12_import_rejects_duplicates_bad_rows_and_other_countries_with_a_downloadable_error_report()
    {
        var dup = $"dup.{Guid.NewGuid():N}@mail.test";
        var form = () => Csv(
            Row("Ok", "One", $"ok.{Guid.NewGuid():N}@mail.test"),
            Row("Twice", "A", dup),
            Row("Twice", "B", dup.ToUpperInvariant()),
            Row("Wrong", "Country", $"us.{Guid.NewGuid():N}@mail.test", country: "US"),
            Row("Bad", "Status", $"bad.{Guid.NewGuid():N}@mail.test", status: "Blacklisted"),
            Row("No", "Contact", ""));

        var result = await ImportAsync(form());
        var report = await factory.CreateClientAs(Hr).PostAsync("/api/resume-bank/import/error-report", form());

        Assert.Equal((6, 0), (result.TotalRows, result.Imported));
        Assert.Contains(result.Errors, e => e.Row == 4 && e.Message.Contains("row 3", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Row == 5 && e.Column == "Country");
        Assert.Contains(result.Errors, e => e.Row == 6 && e.Column == "Status");
        Assert.Contains(result.Errors, e => e.Row == 7);
        Assert.DoesNotContain(result.Errors, e => e.Row == 2);
        Assert.False(await factory.WithDbAsync(db => db.People.AnyAsync(p => p.PersonalEmail == dup)));

        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        using var workbook = new XLWorkbook(new MemoryStream(await report.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheets.First();
        Assert.Contains("Errors", sheet.Row(1).CellsUsed().Select(c => c.GetString()));
        Assert.Equal(7, sheet.LastRowUsed()!.RowNumber());
    }

    [Fact]
    public async Task RB_12_import_finds_people_already_in_the_portal_by_email_or_phone()
    {
        var person = await ImportOneAsync();
        var email = await factory.WithDbAsync(db => db.People.Where(p => p.Id == person).Select(p => p.PersonalEmail).SingleAsync());

        var result = await ImportAsync(Csv(Row("Same", "Person", email!.ToUpperInvariant())));

        Assert.Equal(0, result.Imported);
        Assert.Contains(result.Errors, e => e.Row == 2 && e.Column == "Email");
    }

    [Fact]
    public async Task RB_12_template_lists_the_columns()
    {
        var response = await factory.CreateClientAs(Hr).GetAsync("/api/resume-bank/import/template");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var headers = workbook.Worksheets.First().Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal(Header.Split(','), headers);
    }
}
