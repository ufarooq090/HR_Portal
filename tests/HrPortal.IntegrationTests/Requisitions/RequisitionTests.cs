using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HrPortal.Domain.Requisitions;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Notifications.NotificationEndpoints;
using static HrPortal.Api.Requisitions.ApprovalChainEndpoints;
using static HrPortal.Api.Requisitions.RequisitionEndpoints;

namespace HrPortal.IntegrationTests.Requisitions;

/// <summary>REQ-01..04: raising, approving and managing requisitions, with department and country scoping.</summary>
[Collection(ApiCollection.Name)]
public class RequisitionTests(HrPortalApiFactory factory)
{
    private const string CeoEmail = "dev-ceo@hrportal.test";

    private async Task<RequisitionRequest> RequestAsync(string country = "PK", string department = "ENG", decimal min = 2_000_000, decimal max = 3_000_000)
    {
        var ids = await factory.WithDbAsync(async db => new
        {
            Department = await db.Departments.Where(d => d.Code == department).Select(d => d.Id).SingleAsync(),
            Office = await db.OfficeLocations.Where(o => o.Name == (country == "PK" ? "Lahore Office" : "US Office")).Select(o => o.Id).SingleAsync(),
        });
        return new RequisitionRequest(
            $"Backend Engineer {Guid.NewGuid():N}"[..30], ids.Department, ids.Office, 2, min, max,
            RequisitionReason.NewPosition, null, new DateOnly(2027, 3, 1), "Build and run our APIs.");
    }

    private async Task<RequisitionDetail> CreateAsync(string devUser = "dev-pk-linemanager", RequisitionRequest? request = null)
    {
        var response = await factory.CreateClientAs(devUser).PostAsJsonAsync("/api/requisitions", request ?? await RequestAsync());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RequisitionDetail>())!;
    }

    private async Task<RequisitionDetail> PostAsync(string devUser, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await factory.CreateClientAs(devUser).PostAsJsonAsync(path, body ?? new { });
        Assert.True(expected == response.StatusCode, $"{path} as {devUser}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return expected == HttpStatusCode.OK ? (await response.Content.ReadFromJsonAsync<RequisitionDetail>())! : null!;
    }

    private async Task<RequisitionDetail> SubmittedAsync(string requester = "dev-pk-linemanager", RequisitionRequest? request = null) =>
        await PostAsync(requester, $"/api/requisitions/{(await CreateAsync(requester, request)).Id}/submit");

    /// <summary>Replaces a country's chain for one test; dispose to restore the single CEO approval.</summary>
    private async Task<IAsyncDisposable> ChainAsync(string country, params (string Label, string Number)[] steps)
    {
        async Task SetAsync((string Label, string Number)[] chain)
        {
            var request = new UpdateApprovalChainRequest(
                await Task.WhenAll(chain.Select(async s => new ApprovalStepRequest(s.Label, await factory.EmployeeIdAsync(s.Number)))));
            var response = await factory.CreateClientAs("dev-us-hradmin").PutAsJsonAsync($"/api/approval-chains/{country}", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await SetAsync(steps);
        return new Restore(() => SetAsync([("CEO", "PK-0010")]));
    }

    private sealed class Restore(Func<Task> restore) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await restore();
    }

    [Fact]
    public async Task REQ_01_line_manager_raises_a_draft_for_their_department_in_local_currency()
    {
        var requisition = await CreateAsync();

        Assert.Matches("^REQ-[0-9]{5}$", requisition.Number);
        Assert.Equal(RequisitionStatus.Draft, requisition.Status);
        Assert.Equal("PKR", requisition.CurrencyCode);
        Assert.Equal((2_000_000m, 3_000_000m), (requisition.BudgetMin, requisition.BudgetMax));
        Assert.Equal("Engineering", requisition.DepartmentName);
        Assert.Equal("Dev LineManager (PK)", requisition.RequesterName);
        Assert.Equal(RequisitionStatus.Draft, Assert.Single(requisition.History).ToStatus);
        Assert.True(requisition.Permissions is { CanEdit: true, CanSubmit: true, CanDecide: false });
        Assert.Equal([RequisitionStatus.Cancelled], requisition.Permissions.StatusOptions);
    }

    [Theory]
    [InlineData("dev-pk-linemanager")]
    [InlineData("dev-pk-hiringmanager")]
    public async Task REQ_01_managers_cannot_raise_for_another_department(string devUser)
    {
        var response = await factory.CreateClientAs(devUser).PostAsJsonAsync("/api/requisitions", await RequestAsync(department: "FIN"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("dev-pk-employee")]
    [InlineData("dev-pk-interviewer")]
    [InlineData("dev-pk-systemadmin")]
    [InlineData("dev-ceo")]
    public async Task NFR_02_roles_without_requisition_access_cannot_raise_or_list(string devUser)
    {
        var client = factory.CreateClientAs(devUser);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/requisitions", await RequestAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/requisitions")).StatusCode);
    }

    [Fact]
    public async Task MC_03_HR_raises_for_any_department_in_their_countries_only()
    {
        Assert.Equal("Finance", (await CreateAsync("dev-pk-hrexecutive", await RequestAsync(department: "FIN"))).DepartmentName);

        var abroad = await factory.CreateClientAs("dev-pk-hrexecutive").PostAsJsonAsync("/api/requisitions", await RequestAsync(country: "US"));
        Assert.Equal(HttpStatusCode.Forbidden, abroad.StatusCode);
        Assert.Equal("USD", (await CreateAsync("dev-us-hradmin", await RequestAsync(country: "US", min: 90_000, max: 120_000))).CurrencyCode);
    }

    [Fact]
    public async Task REQ_01_invalid_budget_and_replacement_are_rejected()
    {
        var client = factory.CreateClientAs("dev-pk-linemanager");
        var request = await RequestAsync();
        var employee = await factory.EmployeeIdAsync("PK-0006");
        var finance = await factory.EmployeeIdAsync("PK-0009");

        var badBudget = await client.PostAsJsonAsync("/api/requisitions", request with { BudgetMin = 5, BudgetMax = 4 });
        var noReplacement = await client.PostAsJsonAsync("/api/requisitions", request with { Reason = RequisitionReason.Replacement });
        var otherDepartment = await client.PostAsJsonAsync("/api/requisitions", request with { Reason = RequisitionReason.Replacement, ReplacedEmployeeId = finance });
        var replacement = await client.PostAsJsonAsync("/api/requisitions", request with { Reason = RequisitionReason.Replacement, ReplacedEmployeeId = employee });

        Assert.Equal(HttpStatusCode.BadRequest, badBudget.StatusCode);
        Assert.True((await noReplacement.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("replacedEmployeeId", out _));
        Assert.Equal(HttpStatusCode.BadRequest, otherDepartment.StatusCode);
        Assert.Equal("Dev Employee (PK)", (await replacement.Content.ReadFromJsonAsync<RequisitionDetail>())!.ReplacedEmployeeName);
    }

    [Fact]
    public async Task REQ_02_submission_asks_the_CEO_who_is_notified_in_app_and_by_email()
    {
        var before = factory.Emails.To(CeoEmail).Count;

        var requisition = await SubmittedAsync();

        Assert.Equal(RequisitionStatus.PendingApproval, requisition.Status);
        var step = Assert.Single(requisition.Approvals);
        Assert.Equal(("CEO", "Dev CEO", ApprovalDecision.Pending), (step.Label, step.ApproverName, step.Decision));
        Assert.Contains((await factory.CreateClientAs("dev-ceo").GetFromJsonAsync<List<RequisitionSummary>>("/api/approvals"))!, r => r.Id == requisition.Id);

        var notifications = await factory.CreateClientAs("dev-ceo").GetFromJsonAsync<NotificationList>("/api/notifications");
        var notification = Assert.Single(notifications!.Items, n => n.Link == $"/requisitions/{requisition.Id}");
        Assert.Contains(requisition.Number, notification.Title);
        Assert.False(notification.IsRead);

        var email = factory.Emails.To(CeoEmail).Skip(before).Single(m => m.Subject.Contains(requisition.Number));
        Assert.Contains($"https://hr.test/requisitions/{requisition.Id}", email.HtmlBody);
        Assert.Contains("PKR 2,000,000", email.HtmlBody);
    }

    [Fact]
    public async Task REQ_03_CEO_approval_opens_the_position_and_tells_the_requester()
    {
        var requisition = await SubmittedAsync();

        var approved = await PostAsync("dev-ceo", $"/api/requisitions/{requisition.Id}/decision", new DecisionRequest(true, "Go ahead"));

        Assert.Equal(RequisitionStatus.Open, approved.Status);
        Assert.Equal((ApprovalDecision.Approved, "Go ahead"), (approved.Approvals[0].Decision, approved.Approvals[0].Comment));
        Assert.Equal(
            [RequisitionStatus.Open, RequisitionStatus.PendingApproval, RequisitionStatus.Draft],
            approved.History.Select(h => h.ToStatus));
        Assert.Equal("Dev CEO", approved.History[0].ActorName);
        Assert.DoesNotContain((await factory.CreateClientAs("dev-ceo").GetFromJsonAsync<List<RequisitionSummary>>("/api/approvals"))!, r => r.Id == requisition.Id);

        var mine = await factory.CreateClientAs("dev-pk-linemanager").GetFromJsonAsync<NotificationList>("/api/notifications");
        Assert.Contains(mine!.Items, n => n.Title == $"Requisition approved: {requisition.Number}: {requisition.Title}");
        Assert.Contains(factory.Emails.To("dev-pk-linemanager@hrportal.test"), m => m.Subject.Contains("approved") && m.Subject.Contains(requisition.Number));
    }

    [Fact]
    public async Task REQ_02_rejection_needs_a_comment_returns_to_draft_and_can_be_resubmitted()
    {
        var requisition = await SubmittedAsync();

        await PostAsync("dev-ceo", $"/api/requisitions/{requisition.Id}/decision", new DecisionRequest(false, " "), HttpStatusCode.BadRequest);
        var rejected = await PostAsync("dev-ceo", $"/api/requisitions/{requisition.Id}/decision", new DecisionRequest(false, "Budget too high"));

        Assert.Equal(RequisitionStatus.Draft, rejected.Status);
        Assert.Contains("Budget too high", rejected.History[0].Comment);
        Assert.Contains(factory.Emails.To("dev-pk-linemanager@hrportal.test"), m => m.Subject.Contains("rejected") && m.HtmlBody.Contains("Budget too high"));

        var resubmitted = await PostAsync("dev-pk-linemanager", $"/api/requisitions/{requisition.Id}/submit");
        Assert.Equal(RequisitionStatus.PendingApproval, resubmitted.Status);
        Assert.Equal([(2, ApprovalDecision.Pending), (1, ApprovalDecision.Rejected)], resubmitted.Approvals.Select(a => (a.Round, a.Decision)));
    }

    [Fact]
    public async Task REQ_02_multi_step_chain_runs_in_order_and_notifies_each_approver()
    {
        await using var _ = await ChainAsync("PK", ("CEO", "PK-0010"), ("Head of HR", "PK-0001"));
        var requisition = await SubmittedAsync();

        // Not HR's turn yet.
        await PostAsync("dev-pk-hradmin", $"/api/requisitions/{requisition.Id}/decision", new DecisionRequest(true, null), HttpStatusCode.Forbidden);

        var afterCeo = await PostAsync("dev-ceo", $"/api/requisitions/{requisition.Id}/decision", new DecisionRequest(true, null));
        Assert.Equal(RequisitionStatus.PendingApproval, afterCeo.Status);
        Assert.Contains(factory.Emails.To("dev-pk-hradmin@hrportal.test"), m => m.Subject.Contains(requisition.Number));
        Assert.Contains((await factory.CreateClientAs("dev-pk-hradmin").GetFromJsonAsync<List<RequisitionSummary>>("/api/approvals"))!, r => r.Id == requisition.Id);

        var afterHr = await PostAsync("dev-pk-hradmin", $"/api/requisitions/{requisition.Id}/decision", new DecisionRequest(true, null));
        Assert.Equal(RequisitionStatus.Open, afterHr.Status);
    }

    [Fact]
    public async Task REQ_02_requesters_cannot_approve_their_own_requisition()
    {
        await using var _ = await ChainAsync("PK", ("Line manager", "PK-0005"));
        var requisition = await SubmittedAsync();

        Assert.False(requisition.Permissions.CanDecide);
        await PostAsync("dev-pk-linemanager", $"/api/requisitions/{requisition.Id}/decision", new DecisionRequest(true, null), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task REQ_02_submitting_without_a_chain_explains_how_to_fix_it()
    {
        var requisition = await CreateAsync("dev-us-hiringmanager", await RequestAsync(country: "US", min: 90_000, max: 100_000));
        var chain = await factory.WithDbAsync(async db =>
        {
            var existing = await db.ApprovalChains.SingleAsync(c => c.CountryCode == "US");
            db.ApprovalChains.Remove(existing);
            await db.SaveChangesAsync();
            return existing.Steps;
        });

        try
        {
            var response = await factory.CreateClientAs("dev-us-hiringmanager").PostAsJsonAsync($"/api/requisitions/{requisition.Id}/submit", new { });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("approval chain", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await factory.WithDbAsync(async db =>
            {
                var ceo = await db.Employees.SingleAsync(e => e.Id == chain[0].ApproverEmployeeId);
                db.ApprovalChains.Add(new ApprovalChain("US", [("CEO", ceo)]));
                return await db.SaveChangesAsync();
            });
        }
    }

    [Fact]
    public async Task REQ_04_HR_moves_an_open_position_through_hold_and_fill_and_requesters_can_only_cancel_drafts()
    {
        var requisition = await SubmittedAsync();
        await PostAsync("dev-ceo", $"/api/requisitions/{requisition.Id}/decision", new DecisionRequest(true, null));
        var path = $"/api/requisitions/{requisition.Id}/status";

        await PostAsync("dev-pk-linemanager", path, new StatusRequest(RequisitionStatus.OnHold, null), HttpStatusCode.Forbidden);
        var held = await PostAsync("dev-pk-hrexecutive", path, new StatusRequest(RequisitionStatus.OnHold, "Budget freeze"));
        await PostAsync("dev-pk-hrexecutive", path, new StatusRequest(RequisitionStatus.Open, null));
        var filled = await PostAsync("dev-pk-hrexecutive", path, new StatusRequest(RequisitionStatus.Filled, null));

        Assert.Equal(RequisitionStatus.OnHold, held.Status);
        Assert.Equal(RequisitionStatus.Filled, filled.Status);
        Assert.Empty(filled.Permissions.StatusOptions);
        Assert.Equal(
            [RequisitionStatus.Filled, RequisitionStatus.Open, RequisitionStatus.OnHold, RequisitionStatus.Open, RequisitionStatus.PendingApproval, RequisitionStatus.Draft],
            filled.History.Select(h => h.ToStatus));
        Assert.Equal(("Budget freeze", "Dev HrExecutive (PK)"), (filled.History[2].Comment, filled.History[2].ActorName));
        await PostAsync("dev-pk-hrexecutive", path, new StatusRequest(RequisitionStatus.Open, null), HttpStatusCode.Forbidden);

        var draft = await CreateAsync();
        Assert.Equal(RequisitionStatus.Cancelled, (await PostAsync("dev-pk-linemanager", $"/api/requisitions/{draft.Id}/status", new StatusRequest(RequisitionStatus.Cancelled, "Not needed"))).Status);
    }

    [Fact]
    public async Task REQ_04_only_drafts_are_edited_and_withdrawing_returns_to_draft()
    {
        var requisition = await SubmittedAsync();
        var edit = await factory.CreateClientAs("dev-pk-linemanager").PutAsJsonAsync($"/api/requisitions/{requisition.Id}", await RequestAsync() with { Openings = 5 });
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);

        var withdrawn = await PostAsync("dev-pk-linemanager", $"/api/requisitions/{requisition.Id}/withdraw", new CommentRequest("Adding a role"));
        Assert.Equal(RequisitionStatus.Draft, withdrawn.Status);
        Assert.Equal(ApprovalDecision.Closed, withdrawn.Approvals[0].Decision);
        Assert.DoesNotContain((await factory.CreateClientAs("dev-ceo").GetFromJsonAsync<List<RequisitionSummary>>("/api/approvals"))!, r => r.Id == requisition.Id);

        var edited = await factory.CreateClientAs("dev-pk-linemanager").PutAsJsonAsync($"/api/requisitions/{requisition.Id}", await RequestAsync() with { Openings = 5 });
        Assert.Equal(5, (await edited.Content.ReadFromJsonAsync<RequisitionDetail>())!.Openings);
    }

    [Fact]
    public async Task REQ_04_target_date_must_not_have_passed_when_submitting()
    {
        var requisition = await CreateAsync(request: await RequestAsync() with { TargetDate = new DateOnly(2020, 1, 1) });

        var response = await factory.CreateClientAs("dev-pk-linemanager").PostAsJsonAsync($"/api/requisitions/{requisition.Id}/submit", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NFR_02_managers_see_their_departments_requisitions_in_any_country_and_HR_their_countries()
    {
        var engineering = await CreateAsync("dev-pk-hiringmanager");
        var finance = await CreateAsync("dev-pk-hrexecutive", await RequestAsync(department: "FIN"));

        async Task<List<Guid>> ListAsync(string devUser) =>
            (await factory.CreateClientAs(devUser).GetFromJsonAsync<List<RequisitionSummary>>("/api/requisitions"))!.Select(r => r.Id).ToList();

        // Departments span countries (decision 13 Oct 2026), so the US engineering line manager sees PK engineering.
        foreach (var manager in new[] { "dev-pk-linemanager", "dev-us-linemanager", "dev-us-hiringmanager" })
        {
            var visible = await ListAsync(manager);
            Assert.Contains(engineering.Id, visible);
            Assert.DoesNotContain(finance.Id, visible);
            Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs(manager).GetAsync($"/api/requisitions/{finance.Id}")).StatusCode);
        }

        Assert.Contains(finance.Id, await ListAsync("dev-pk-hrexecutive"));
        Assert.Contains(finance.Id, await ListAsync("dev-us-hradmin"));
        Assert.DoesNotContain(finance.Id, await ListAsync("dev-us-hrexecutive"));
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs("dev-us-hrexecutive").GetAsync($"/api/requisitions/{finance.Id}")).StatusCode);
    }

    [Fact]
    public async Task NFR_02_approvers_see_only_what_they_are_asked_to_approve()
    {
        var draft = await CreateAsync();
        var submitted = await SubmittedAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs("dev-ceo").GetAsync($"/api/requisitions/{draft.Id}")).StatusCode);
        var asCeo = await factory.CreateClientAs("dev-ceo").GetFromJsonAsync<RequisitionDetail>($"/api/requisitions/{submitted.Id}");
        Assert.True(asCeo!.Permissions is { CanDecide: true, CanEdit: false, CanWithdraw: false });
        Assert.Empty(asCeo.Permissions.StatusOptions);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs("dev-pk-employee").GetAsync($"/api/requisitions/{submitted.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClientAs("dev-ceo").GetAsync($"/api/requisitions/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Notifications_belong_to_their_recipient()
    {
        await SubmittedAsync();
        var ceo = factory.CreateClientAs("dev-ceo");
        var notification = (await ceo.GetFromJsonAsync<NotificationList>("/api/notifications"))!.Items[0];

        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClientAs("dev-pk-employee").PostAsync($"/api/notifications/{notification.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ceo.PostAsync($"/api/notifications/{notification.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ceo.PostAsync("/api/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, (await ceo.GetFromJsonAsync<NotificationList>("/api/notifications"))!.UnreadCount);
    }

    [Fact]
    public async Task Email_content_from_users_is_html_encoded()
    {
        var requisition = await SubmittedAsync(request: await RequestAsync() with { Title = "<script>alert(1)</script>" });

        var email = factory.Emails.To(CeoEmail).Single(m => m.Subject.Contains(requisition.Number));

        Assert.DoesNotContain("<script>", email.HtmlBody);
        Assert.Contains("&lt;script&gt;", email.HtmlBody);
    }

    [Theory]
    [MemberData(nameof(SeedData.DevUsersExceptHrAdmins), MemberType = typeof(SeedData))]
    public async Task REQ_02_only_HR_admins_change_approval_chains(string devUser)
    {
        var request = new UpdateApprovalChainRequest([new ApprovalStepRequest("CEO", await factory.EmployeeIdAsync("PK-0010"))]);

        var response = await factory.CreateClientAs(devUser).PutAsJsonAsync("/api/approval-chains/PK", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task REQ_02_chain_changes_are_validated_and_limited_to_the_admins_countries()
    {
        var ceo = await factory.EmployeeIdAsync("PK-0010");
        var duplicate = new UpdateApprovalChainRequest([new("CEO", ceo), new("Again", ceo)]);
        var admin = factory.CreateClientAs("dev-us-hradmin");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/approval-chains/PK", duplicate)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/approval-chains/PK", new UpdateApprovalChainRequest([]))).StatusCode);

        await using var _ = await factory.SetHrAccessAsync("dev-pk-hradmin", Domain.Security.CountryScope.Of(["PK"]));
        var limited = await factory.CreateClientAs("dev-pk-hradmin").PutAsJsonAsync("/api/approval-chains/US", new UpdateApprovalChainRequest([new("CEO", ceo)]));
        Assert.Equal(HttpStatusCode.Forbidden, limited.StatusCode);

        var chains = await factory.CreateClientAs("dev-pk-hrexecutive").GetFromJsonAsync<List<ApprovalChainDto>>("/api/approval-chains");
        Assert.All(chains!.Where(c => c.CountryCode is "PK" or "US"), c => Assert.Equal(("CEO", "Dev CEO"), (c.Steps.Single().Label, c.Steps.Single().ApproverName)));
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientAs("dev-pk-linemanager").GetAsync("/api/approval-chains")).StatusCode);
    }

    [Fact]
    public async Task REQ_02_the_api_refuses_to_remove_the_last_step_of_a_chain()
    {
        var admin = factory.CreateClientAs("dev-us-hradmin");

        var empty = await admin.PutAsJsonAsync("/api/approval-chains/PK", new UpdateApprovalChainRequest([]));
        var missing = await admin.PutAsJsonAsync("/api/approval-chains/PK", new { steps = (object?)null });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var errors = (await empty.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("steps");
        Assert.Equal("An approval chain needs at least one step.", errors[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var chains = await admin.GetFromJsonAsync<List<ApprovalChainDto>>("/api/approval-chains");
        Assert.Equal(("CEO", "Dev CEO"), chains!.Single(c => c.CountryCode == "PK").Steps.Select(s => (s.Label, s.ApproverName)).Single());
    }

    [Fact]
    public async Task Me_reports_who_is_an_approver()
    {
        var ceo = await factory.CreateClientAs("dev-ceo").GetFromJsonAsync<Api.Auth.MeEndpoints.MeResponse>("/api/me");
        var manager = await factory.CreateClientAs("dev-pk-linemanager").GetFromJsonAsync<Api.Auth.MeEndpoints.MeResponse>("/api/me");

        Assert.True(ceo!.IsApprover);
        Assert.False(manager!.IsApprover);
    }
}
