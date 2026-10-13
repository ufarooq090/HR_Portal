using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Recruiting.ApplicationEndpoints;
using static HrPortal.Api.Recruiting.CandidateEndpoints;
using static HrPortal.Api.Requisitions.RequisitionEndpoints;

namespace HrPortal.IntegrationTests.Recruiting;

/// <summary>CT-01..03, CT-06..09: candidates, pipeline, interviews, scorecards and notes, with recruiting scopes.</summary>
[Collection(ApiCollection.Name)]
public class CandidateTrackerTests(HrPortalApiFactory factory)
{
    private const string Hr = "dev-pk-hrexecutive";
    private static readonly Guid SkillsMismatch = new("0199c3a0-0000-7000-8000-000000000001");

    /// <summary>An approved (open) requisition raised by a hiring manager.</summary>
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

    private static CandidateRequest NewCandidate(string? email = null, string? phone = null) =>
        new("Sana", $"Iqbal {Guid.NewGuid():N}"[..14], email ?? $"sana.{Guid.NewGuid():N}@mail.test", phone, "Acme Ltd", 30,
            new MoneyDto(2_400_000, "PKR"), new MoneyDto(3_000_000, "PKR"));

    private async Task<ApplicationCard> AddAsync(Guid requisitionId, CandidateRequest? candidate = null, Guid? personId = null, string devUser = Hr)
    {
        var response = await factory.CreateClientAs(devUser).PostAsJsonAsync(
            $"/api/requisitions/{requisitionId}/applications", new AddApplicationRequest(personId, personId is null ? candidate ?? NewCandidate() : null, CandidateSource.Referral));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApplicationCard>())!;
    }

    private async Task<ApplicationDetail> PostAsync(string devUser, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await factory.CreateClientAs(devUser).PostAsJsonAsync(path, body);
        Assert.True(expected == response.StatusCode, $"{path} as {devUser}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return expected == HttpStatusCode.OK ? (await response.Content.ReadFromJsonAsync<ApplicationDetail>())! : null!;
    }

    private async Task<ApplicationDetail> ScheduleAsync(Guid applicationId, int round, params string[] panelNumbers)
    {
        var panel = await Task.WhenAll(panelNumbers.Select(factory.EmployeeIdAsync));
        return await PostAsync(Hr, $"/api/applications/{applicationId}/interviews", new ScheduleInterviewRequest(
            round, new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero), 60, InterviewMode.Teams, "Teams link", panel));
    }

    private static ScorecardRequest Feedback(IReadOnlyList<string> criteria, int rating = 4, Recommendation recommendation = Recommendation.Hire) =>
        new(criteria.Select(c => new CriterionRating(c, rating, null)).ToList(), recommendation, "Solid.");

    [Fact]
    public async Task CT_02_HR_adds_a_candidate_with_contact_details_and_local_currency_salary()
    {
        var requisition = await OpenRequisitionAsync();

        var card = await AddAsync(requisition.Id, NewCandidate("Sana.New@Mail.test", "0300 1112233"));

        Assert.Equal((PipelineStage.Applied, CandidateSource.Referral, "Acme Ltd"), (card.Stage, card.Source, card.CurrentEmployer));
        var detail = await factory.CreateClientAs(Hr).GetFromJsonAsync<CandidateDetail>($"/api/candidates/{card.PersonId}");
        Assert.Equal(("sana.new@mail.test", "0300 1112233", 30), (detail!.Email, detail.Phone, detail.NoticePeriodDays));
        Assert.Equal(new MoneyDto(3_000_000, "PKR"), detail.ExpectedCtc);
        Assert.True(detail.CanEdit);
    }

    [Fact]
    public async Task MC_02_candidate_salary_must_be_in_a_country_currency_and_is_encrypted_and_its_views_audited()
    {
        var requisition = await OpenRequisitionAsync();
        var bad = await factory.CreateClientAs(Hr).PostAsJsonAsync($"/api/requisitions/{requisition.Id}/applications",
            new AddApplicationRequest(null, NewCandidate() with { CurrentCtc = new MoneyDto(50_000, "EUR") }, CandidateSource.Portal));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var card = await AddAsync(requisition.Id, NewCandidate() with { ExpectedCtc = new MoneyDto(4_444_444, "PKR") });
        var raw = await factory.WithDbAsync(db => db.Database
            .SqlQuery<string>($"""SELECT "ExpectedCtcAmount" AS "Value" FROM "CandidateProfiles" WHERE "PersonId" = {card.PersonId}""").SingleAsync());
        Assert.DoesNotContain("4444444", raw);

        await factory.CreateClientAs("dev-pk-hiringmanager").GetAsync($"/api/applications/{card.Id}");
        Assert.True(await factory.WithDbAsync(db => db.AuditEntries.AnyAsync(a =>
            a.Action == AuditAction.Viewed && a.EntityType == CompensationAuditType && a.EntityId == card.PersonId.ToString() && a.ActorId == "dev-pk-hiringmanager")));
    }

    [Fact]
    public async Task CT_09_duplicates_on_email_or_phone_are_refused_and_link_to_the_existing_person()
    {
        var first = await OpenRequisitionAsync();
        var second = await OpenRequisitionAsync();
        var original = await AddAsync(first.Id, NewCandidate("ayesha.dup@mail.test", "+92 300 555 0101"));

        var sameEmail = await factory.CreateClientAs(Hr).PostAsJsonAsync($"/api/requisitions/{second.Id}/applications",
            new AddApplicationRequest(null, NewCandidate(" Ayesha.DUP@mail.test "), CandidateSource.Portal));
        var samePhone = await factory.CreateClientAs(Hr).PostAsJsonAsync($"/api/requisitions/{second.Id}/applications",
            new AddApplicationRequest(null, NewCandidate(phone: "0300-5550101"), CandidateSource.Portal));

        Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, samePhone.StatusCode);
        var match = (await samePhone.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicates")[0];
        Assert.Equal(original.PersonId, match.GetProperty("personId").GetGuid());
        Assert.True(match.GetProperty("matchedOnPhone").GetBoolean());

        var linked = await AddAsync(second.Id, personId: original.PersonId);
        Assert.Equal(original.PersonId, linked.PersonId);
        var again = await factory.CreateClientAs(Hr).PostAsJsonAsync($"/api/requisitions/{second.Id}/applications", new AddApplicationRequest(original.PersonId, null, CandidateSource.Portal));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(2, (await factory.CreateClientAs(Hr).GetFromJsonAsync<CandidateDetail>($"/api/candidates/{original.PersonId}"))!.Applications.Count);
    }

    [Fact]
    public async Task CT_09_an_employee_applying_is_the_same_person_rule_9()
    {
        // The seeded PK employee's phone (PK-0006).
        var matches = await (await factory.CreateClientAs(Hr).PostAsJsonAsync("/api/candidates/duplicates", new DuplicateCheckRequest(null, "03000000006")))
            .Content.ReadFromJsonAsync<List<DuplicateMatch>>();

        var match = Assert.Single(matches!);
        Assert.True(match.IsEmployee);
        Assert.Equal("Dev Employee (PK)", match.Name);
    }

    [Theory]
    [InlineData("dev-pk-hradmin", true)]
    [InlineData("dev-us-hradmin", true)]
    [InlineData("dev-pk-hrexecutive", true)]
    [InlineData("dev-pk-hiringmanager", true)]
    [InlineData("dev-us-hrexecutive", false)]
    [InlineData("dev-us-hiringmanager", false)]
    [InlineData("dev-pk-linemanager", false)]
    [InlineData("dev-pk-interviewer", false)]
    [InlineData("dev-pk-employee", false)]
    [InlineData("dev-pk-systemadmin", false)]
    [InlineData("dev-ceo", false)]
    public async Task NFR_02_HR_in_country_and_the_hiring_manager_see_applications_others_do_not(string devUser, bool allowed)
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id);
        var client = factory.CreateClientAs(devUser);

        var detail = await client.GetAsync($"/api/applications/{card.Id}");
        var candidate = await client.GetAsync($"/api/candidates/{card.PersonId}");
        var list = await client.GetFromJsonAsync<List<ApplicationCard>>($"/api/candidates?requisitionId={requisition.Id}");

        var expected = allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        Assert.Equal(expected, detail.StatusCode);
        Assert.Equal(expected, candidate.StatusCode);
        Assert.Equal(allowed, list!.Any(a => a.Id == card.Id));
    }

    [Fact]
    public async Task NFR_02_interviewers_see_only_candidates_whose_panel_they_are_on()
    {
        var requisition = await OpenRequisitionAsync();
        var mine = await AddAsync(requisition.Id);
        var other = await AddAsync(requisition.Id);
        var interviewer = factory.CreateClientAs("dev-pk-interviewer");

        var scheduled = await ScheduleAsync(mine.Id, 1, "PK-0007");

        Assert.Equal(HttpStatusCode.OK, (await interviewer.GetAsync($"/api/applications/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await interviewer.GetAsync($"/api/applications/{other.Id}")).StatusCode);
        var board = await interviewer.GetFromJsonAsync<BoardResponse>($"/api/requisitions/{requisition.Id}/applications");
        Assert.Equal([mine.Id], board!.Applications.Select(a => a.Id));
        Assert.False(board.CanManage);

        // Cancelling the interview removes the assignment.
        await factory.CreateClientAs(Hr).DeleteAsync($"/api/applications/{mine.Id}/interviews/{scheduled.Interviews[0].Id}");
        Assert.Equal(HttpStatusCode.Forbidden, (await interviewer.GetAsync($"/api/applications/{mine.Id}")).StatusCode);
    }

    [Fact]
    public async Task NFR_02_panelists_do_not_see_salary_notes_or_stage_history_and_cannot_move_candidates()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id);
        await PostAsync(Hr, $"/api/applications/{card.Id}/notes", new NoteRequest("Strong referral from the platform team."));
        await ScheduleAsync(card.Id, 1, "PK-0007", "PK-0006");

        foreach (var panelist in new[] { "dev-pk-interviewer", "dev-pk-employee" })
        {
            var detail = await factory.CreateClientAs(panelist).GetFromJsonAsync<ApplicationDetail>($"/api/applications/{card.Id}");
            Assert.Null(detail!.Candidate.ExpectedCtc);
            Assert.Empty(detail.Notes);
            Assert.Empty(detail.History);
            Assert.True(detail.Permissions is { CanView: true, CanManage: false, CanSeeNotes: false, CanSeeCompensation: false });
            await PostAsync(panelist, $"/api/applications/{card.Id}/notes", new NoteRequest("x"), HttpStatusCode.Forbidden);
            await PostAsync(panelist, $"/api/applications/{card.Id}/stage", new StageRequest(PipelineStage.Screening, null, null, null), HttpStatusCode.Forbidden);
        }

        var asHiringManager = await factory.CreateClientAs("dev-pk-hiringmanager").GetFromJsonAsync<ApplicationDetail>($"/api/applications/{card.Id}");
        Assert.Equal("Strong referral from the platform team.", Assert.Single(asHiringManager!.Notes).Body);
        Assert.NotNull(asHiringManager.Candidate.ExpectedCtc);
    }

    [Fact]
    public async Task CT_03_stage_changes_are_logged_with_user_time_and_reason()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id);
        var path = $"/api/applications/{card.Id}/stage";

        await PostAsync(Hr, path, new StageRequest(PipelineStage.Screening, null, null, "CV looks good"));
        await PostAsync(Hr, path, new StageRequest(PipelineStage.Interview, null, null, null), HttpStatusCode.BadRequest);
        await PostAsync(Hr, path, new StageRequest(PipelineStage.Interview, 3, null, null), HttpStatusCode.BadRequest);
        await PostAsync(Hr, path, new StageRequest(PipelineStage.Interview, 2, null, null));
        await PostAsync(Hr, path, new StageRequest(PipelineStage.Joined, null, null, null), HttpStatusCode.BadRequest);
        await PostAsync(Hr, path, new StageRequest(PipelineStage.Rejected, null, null, "No reason"), HttpStatusCode.BadRequest);
        var rejected = await PostAsync(Hr, path, new StageRequest(PipelineStage.Rejected, null, SkillsMismatch, "Lacks Spark experience"));

        Assert.Equal(PipelineStage.Rejected, rejected.Stage);
        var latest = rejected.History[0];
        Assert.Equal((PipelineStage.Interview, 2, PipelineStage.Rejected), (latest.FromStage!.Value, latest.FromRound!.Value, latest.ToStage));
        Assert.Equal(("Skills do not match the role", "Lacks Spark experience", "Dev HrExecutive (PK)"), (latest.RejectionReason, latest.Reason, latest.ActorName));
        Assert.Equal([PipelineStage.Rejected, PipelineStage.Interview, PipelineStage.Screening, PipelineStage.Applied], rejected.History.Select(h => h.ToStage));
    }

    [Fact]
    public async Task CT_03_offer_path_ends_at_joined_which_is_final()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id);
        var path = $"/api/applications/{card.Id}/stage";

        await PostAsync(Hr, path, new StageRequest(PipelineStage.OfferAccepted, null, null, null), HttpStatusCode.BadRequest);
        await PostAsync(Hr, path, new StageRequest(PipelineStage.Offer, null, null, null));
        await PostAsync(Hr, path, new StageRequest(PipelineStage.OfferAccepted, null, null, null));
        var joined = await PostAsync(Hr, path, new StageRequest(PipelineStage.Joined, null, null, null));

        Assert.Equal(PipelineStage.Joined, joined.Stage);
        await PostAsync(Hr, path, new StageRequest(PipelineStage.Withdrawn, null, null, null), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("dev-pk-hiringmanager")]
    [InlineData("dev-pk-linemanager")]
    [InlineData("dev-pk-interviewer")]
    [InlineData("dev-us-hrexecutive")]
    public async Task NFR_02_only_HR_in_country_adds_candidates_or_moves_them(string devUser)
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id);
        var client = factory.CreateClientAs(devUser);

        var add = await client.PostAsJsonAsync($"/api/requisitions/{requisition.Id}/applications", new AddApplicationRequest(null, NewCandidate(), CandidateSource.Portal));
        var move = await client.PostAsJsonAsync($"/api/applications/{card.Id}/stage", new StageRequest(PipelineStage.Screening, null, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, move.StatusCode);
    }

    [Fact]
    public async Task REQ_03_candidates_are_only_added_to_open_requisitions()
    {
        var hiringManager = factory.CreateClientAs("dev-pk-hiringmanager");
        var ids = await factory.WithDbAsync(async db => new
        {
            Department = await db.Departments.Where(d => d.Code == "ENG").Select(d => d.Id).SingleAsync(),
            Office = await db.OfficeLocations.Where(o => o.Name == "Lahore Office").Select(o => o.Id).SingleAsync(),
        });
        var draft = await (await hiringManager.PostAsJsonAsync("/api/requisitions", new RequisitionRequest(
            "Draft role", ids.Department, ids.Office, 1, 1, 2, RequisitionReason.NewPosition, null, new DateOnly(2030, 1, 1), "x"))).Content.ReadFromJsonAsync<RequisitionDetail>();

        var response = await factory.CreateClientAs(Hr).PostAsJsonAsync($"/api/requisitions/{draft!.Id}/applications", new AddApplicationRequest(null, NewCandidate(), CandidateSource.Portal));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CT_04_interviews_go_through_the_calendar_service_and_notify_the_panel()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id, NewCandidate("cal.candidate@mail.test"));

        var detail = await ScheduleAsync(card.Id, 1, "PK-0007");

        var interview = Assert.Single(detail.Interviews);
        Assert.Equal((1, InterviewMode.Teams, "Teams link"), (interview.Round, interview.Mode, interview.Location));
        Assert.Equal(["Dev Interviewer (PK)"], interview.PanelNames);
        var created = factory.Calendar.Created.Single(e => e.Event.Attendees.Contains("cal.candidate@mail.test"));
        Assert.Contains("dev-pk-interviewer@hrportal.test", created.Event.Attendees);
        Assert.Equal("dev-pk-hrexecutive@hrportal.test", created.Event.OrganizerEmail);
        Assert.True(created.Event.IsOnlineMeeting);
        Assert.Contains(factory.Emails.To("dev-pk-interviewer@hrportal.test"), m => m.Subject.Contains(card.CandidateName));

        var mine = await factory.CreateClientAs("dev-pk-interviewer").GetFromJsonAsync<List<MyInterview>>("/api/interviews/mine");
        Assert.Contains(mine!, i => i.InterviewId == interview.Id && !i.ScorecardSubmitted);

        await factory.CreateClientAs(Hr).DeleteAsync($"/api/applications/{card.Id}/interviews/{interview.Id}");
        Assert.Contains(created.EventId, factory.Calendar.Cancelled);
    }

    [Fact]
    public async Task CT_04_each_panelist_gets_the_time_in_their_own_office_time_zone()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id);

        // 10:00 UTC on 2 Nov 2026: 15:00 in Karachi, 05:00 in New York (after the US clocks change).
        await ScheduleAsync(card.Id, 1, "PK-0007", "US-0007");

        var pk = factory.Emails.To("dev-pk-interviewer@hrportal.test").Last(m => m.Subject.Contains(card.CandidateName));
        var us = factory.Emails.To("dev-us-interviewer@hrportal.test").Last(m => m.Subject.Contains(card.CandidateName));
        Assert.Contains("Mon 2 Nov 2026, 15:00 Asia/Karachi (UTC+05:00)", pk.HtmlBody);
        Assert.Contains("Mon 2 Nov 2026, 05:00 America/New_York (UTC-05:00)", us.HtmlBody);
        Assert.DoesNotContain(" UTC,", pk.HtmlBody);
    }

    [Fact]
    public async Task CT_07_panelists_see_others_feedback_only_after_submitting_their_own()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id);
        var interview = (await ScheduleAsync(card.Id, 1, "PK-0007", "PK-0006")).Interviews.Single();
        var path = $"/api/interviews/{interview.Id}/scorecard";

        var afterFirst = await PostAsync("dev-pk-interviewer", path, Feedback(interview.Criteria, 5, Recommendation.StrongHire));
        Assert.Equal(["Dev Interviewer (PK)"], afterFirst.Interviews.Single().Scorecards.Select(s => s.InterviewerName));

        var beforeSecond = await factory.CreateClientAs("dev-pk-employee").GetFromJsonAsync<ApplicationDetail>($"/api/applications/{card.Id}");
        var hidden = beforeSecond!.Interviews.Single();
        Assert.True(hidden.FeedbackHidden);
        Assert.Empty(hidden.Scorecards);
        Assert.Equal(1, hidden.SubmittedCount);

        var asHr = await factory.CreateClientAs(Hr).GetFromJsonAsync<ApplicationDetail>($"/api/applications/{card.Id}");
        var asHiringManager = await factory.CreateClientAs("dev-pk-hiringmanager").GetFromJsonAsync<ApplicationDetail>($"/api/applications/{card.Id}");
        Assert.Single(asHr!.Interviews.Single().Scorecards);
        Assert.Single(asHiringManager!.Interviews.Single().Scorecards);

        var afterSecond = await PostAsync("dev-pk-employee", path, Feedback(interview.Criteria, 3, Recommendation.NoHire));
        Assert.Equal(2, afterSecond.Interviews.Single().Scorecards.Count);
        Assert.False(afterSecond.Interviews.Single().FeedbackHidden);
    }

    [Fact]
    public async Task CT_06_scorecards_rate_every_criterion_1_to_5_once_and_only_panelists_submit()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id);
        var interview = (await ScheduleAsync(card.Id, 1, "PK-0007")).Interviews.Single();
        var path = $"/api/interviews/{interview.Id}/scorecard";
        var criteria = interview.Criteria;

        Assert.Equal(Requisition.DefaultScorecardCriteria, criteria);
        await PostAsync("dev-pk-interviewer", path, Feedback(criteria.Skip(1).ToList()), HttpStatusCode.BadRequest);
        await PostAsync("dev-pk-interviewer", path, Feedback(criteria, 6), HttpStatusCode.BadRequest);
        await PostAsync("dev-pk-hiringmanager", path, Feedback(criteria), HttpStatusCode.Forbidden);
        await PostAsync("dev-pk-interviewer", path, Feedback(criteria));
        await PostAsync("dev-pk-interviewer", path, Feedback(criteria, 1), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CT_06_pipeline_rounds_and_criteria_are_set_per_requisition_and_interviews_keep_their_criteria()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id);
        var before = (await ScheduleAsync(card.Id, 2, "PK-0007")).Interviews.Single();
        var client = factory.CreateClientAs(Hr);

        var shrink = await client.PutAsJsonAsync($"/api/requisitions/{requisition.Id}/pipeline", new PipelineRequest(1, ["Coding"]));
        var configured = await client.PutAsJsonAsync($"/api/requisitions/{requisition.Id}/pipeline", new PipelineRequest(3, ["Coding", "System design"]));
        var asManager = await factory.CreateClientAs("dev-pk-hiringmanager").PutAsJsonAsync($"/api/requisitions/{requisition.Id}/pipeline", new PipelineRequest(3, ["x"]));

        Assert.Equal(HttpStatusCode.BadRequest, shrink.StatusCode);
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asManager.StatusCode);
        var after = (await ScheduleAsync(card.Id, 3, "PK-0007")).Interviews.Single(i => i.Round == 3);
        Assert.Equal(["Coding", "System design"], after.Criteria);
        Assert.Equal(Requisition.DefaultScorecardCriteria, before.Criteria);
    }

    [Fact]
    public async Task CT_03_rejection_reasons_are_managed_by_HR_admins_and_inactive_ones_cannot_be_used()
    {
        var admin = factory.CreateClientAs("dev-pk-hradmin");
        var created = await (await admin.PostAsJsonAsync("/api/recruiting/rejection-reasons", new Api.Recruiting.RejectionReasonEndpoints.RejectionReasonRequest($"Visa {Guid.NewGuid():N}"[..20], 70)))
            .Content.ReadFromJsonAsync<Api.Recruiting.RejectionReasonEndpoints.RejectionReasonDto>();
        await admin.PutAsJsonAsync($"/api/recruiting/rejection-reasons/{created!.Id}", new Api.Recruiting.RejectionReasonEndpoints.RejectionReasonRequest(created.Name, 70, false));

        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id);
        await PostAsync(Hr, $"/api/applications/{card.Id}/stage", new StageRequest(PipelineStage.Rejected, null, created.Id, null), HttpStatusCode.BadRequest);

        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs(Hr).PostAsJsonAsync("/api/recruiting/rejection-reasons",
            new Api.Recruiting.RejectionReasonEndpoints.RejectionReasonRequest("Nope", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClientAs("dev-pk-hiringmanager").GetAsync("/api/recruiting/rejection-reasons")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs("dev-pk-employee").GetAsync("/api/recruiting/rejection-reasons")).StatusCode);
    }

    [Fact]
    public async Task CT_08_notes_have_author_and_time_and_hiring_managers_can_add_them()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id);

        await PostAsync("dev-pk-hiringmanager", $"/api/applications/{card.Id}/notes", new NoteRequest("Met at a meetup; keen."));
        var detail = await PostAsync(Hr, $"/api/applications/{card.Id}/notes", new NoteRequest("Called; available in 30 days."));

        Assert.Equal(["Dev HrExecutive (PK)", "Dev HiringManager (PK)"], detail.Notes.Select(n => n.AuthorName));
        Assert.All(detail.Notes, n => Assert.Equal(TimeSpan.Zero, n.CreatedAtUtc.Offset));
    }

    [Fact]
    public async Task CT_02_CVs_are_PDF_or_Word_uploaded_by_HR_and_readable_by_panelists()
    {
        var requisition = await OpenRequisitionAsync();
        var card = await AddAsync(requisition.Id);
        await ScheduleAsync(card.Id, 1, "PK-0007");
        MultipartFormDataContent Form(byte[] bytes, string name)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return new MultipartFormDataContent { { file, "file", name } };
        }

        var png = await factory.CreateClientAs(Hr).PostAsync($"/api/candidates/{card.PersonId}/cvs", Form([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "photo.png"));
        var asInterviewer = await factory.CreateClientAs("dev-pk-interviewer").PostAsync($"/api/candidates/{card.PersonId}/cvs", Form("%PDF-1.7"u8.ToArray(), "cv.pdf"));
        var uploaded = await factory.CreateClientAs(Hr).PostAsync($"/api/candidates/{card.PersonId}/cvs", Form("%PDF-1.7 cv"u8.ToArray(), "sana-cv.pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, png.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asInterviewer.StatusCode);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var cv = (await uploaded.Content.ReadFromJsonAsync<CvDto>())!;
        var download = await factory.CreateClientAs("dev-pk-interviewer").GetAsync($"/api/candidates/{card.PersonId}/cvs/{cv.Id}/content");
        Assert.Equal("%PDF-1.7 cv"u8.ToArray(), await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs("dev-pk-linemanager").GetAsync($"/api/candidates/{card.PersonId}/cvs/{cv.Id}/content")).StatusCode);
    }

    [Fact]
    public async Task CT_01_list_view_filters_by_stage_source_and_search()
    {
        var requisition = await OpenRequisitionAsync();
        var screened = await AddAsync(requisition.Id, NewCandidate() with { CurrentEmployer = "Zebra Analytics" });
        await AddAsync(requisition.Id);
        await PostAsync(Hr, $"/api/applications/{screened.Id}/stage", new StageRequest(PipelineStage.Screening, null, null, null));
        var client = factory.CreateClientAs(Hr);

        var byStage = await client.GetFromJsonAsync<List<ApplicationCard>>($"/api/candidates?requisitionId={requisition.Id}&stage=Screening");
        var bySearch = await client.GetFromJsonAsync<List<ApplicationCard>>("/api/candidates?search=zebra");
        var bySource = await client.GetFromJsonAsync<List<ApplicationCard>>($"/api/candidates?requisitionId={requisition.Id}&source=Agency");

        Assert.Equal([screened.Id], byStage!.Select(a => a.Id));
        Assert.Contains(bySearch!, a => a.Id == screened.Id);
        Assert.Empty(bySource!);
    }
}
