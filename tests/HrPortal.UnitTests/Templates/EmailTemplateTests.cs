using HrPortal.Domain.Common;
using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Security;
using HrPortal.Domain.Templates;
using HrPortal.Infrastructure.Templates;

namespace HrPortal.UnitTests.Templates;

public class EmailTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly Actor Admin = new("admin", "HR Admin", null);

    private static TemplateContent Content(
        string body = "<p>Dear {{first_name}}, welcome to {{company_name}}.</p>",
        TemplateCategory category = TemplateCategory.Interview,
        TemplateRecordType recordType = TemplateRecordType.Application,
        bool ccManager = false) =>
        new("Interview invite", category, recordType, "Interview for {{designation}}", body, null, SenderMailbox.HrMailbox, ccManager, true);

    private static UserAccess User(string[] roles, string country = "PK") => new(roles.ToHashSet(), Guid.NewGuid(), Guid.NewGuid(), country, null);

    [Fact]
    public void ET_02_merge_fields_fill_with_html_encoded_values_and_missing_ones_are_reported()
    {
        var values = new Dictionary<string, string?> { ["first_name"] = "Sana <b>", ["designation"] = "Engineer", ["panel_names"] = " " };

        var body = MergeFields.Fill("<p>Hi {{ first_name }}, {{designation}} with {{panel_names}} on {{interview_date}}</p>", values, html: true);
        var subject = MergeFields.Fill("Hi {{first_name}}", values, html: false);

        Assert.Equal("<p>Hi Sana &lt;b&gt;, Engineer with {{panel_names}} on {{interview_date}}</p>", body.Text);
        Assert.Equal(["panel_names", "interview_date"], body.Missing);
        Assert.Equal("Hi Sana <b>", subject.Text);
        Assert.Empty(subject.Missing);
    }

    [Theory]
    [InlineData(TemplateRecordType.Application, "interview_date")]
    [InlineData(TemplateRecordType.Employee, "probation_end_date")]
    [InlineData(TemplateRecordType.Requisition, "requisition_link")]
    public void ET_02_each_record_type_offers_its_own_fields(TemplateRecordType type, string field)
    {
        Assert.Contains(MergeFields.For(type), f => f.Key == field);
        Assert.Contains(MergeFields.For(type), f => f.Key == "first_name");
        Assert.Empty(MergeFields.UnknownIn($"{{{{{field}}}}}", type));
    }

    [Fact]
    public void ET_02_templates_reject_fields_their_record_type_cannot_fill()
    {
        var error = Assert.Throws<DomainValidationException>(() =>
            EmailTemplate.Create(Content("<p>{{probation_end_date}} {{frist_name}}</p>"), Admin, Now));

        Assert.Contains("{{probation_end_date}}", error.Message);
        Assert.Contains("{{frist_name}}", error.Message);
    }

    [Fact]
    public void ET_06_every_change_is_a_new_version_and_unchanged_saves_are_not()
    {
        var template = EmailTemplate.Create(Content(), Admin, Now);

        Assert.False(template.Update(Content(), Admin, Now.AddHours(1)));
        Assert.True(template.Update(Content() with { Subject = "New subject", IsActive = false }, new Actor("b", "Other admin", null), Now.AddHours(2)));

        Assert.Equal(2, template.Version);
        Assert.Equal([1, 2], template.Versions.Select(v => v.Version));
        Assert.Equal(("Interview for {{designation}}", true), (template.Versions[0].Subject, template.Versions[0].IsActive));
        Assert.Equal(("New subject", "Other admin"), (template.Versions[1].Subject, template.Versions[1].ChangedByName));
        Assert.Throws<DomainValidationException>(() => template.Update(Content() with { RecordType = TemplateRecordType.Employee }, Admin, Now));
    }

    [Fact]
    public void ET_termination_goes_to_employees_and_only_employee_emails_copy_the_manager()
    {
        Assert.Throws<DomainValidationException>(() => EmailTemplate.Create(Content(category: TemplateCategory.Termination), Admin, Now));
        Assert.Throws<DomainValidationException>(() => EmailTemplate.Create(Content(ccManager: true), Admin, Now));
        Assert.True(EmailTemplate.Create(Content("<p>{{first_name}}</p>", TemplateCategory.Termination, TemplateRecordType.Employee), Admin, Now).IsRestricted);
    }

    [Fact]
    public void Rule_8_termination_is_HR_admin_only_and_never_in_bulk()
    {
        var termination = EmailTemplate.Create(Content("<p>{{first_name}}</p>", TemplateCategory.Termination, TemplateRecordType.Employee), Admin, Now);
        var hr = new SendFacts("PK", IsHr: true, IsHiringManager: false);

        Assert.Null(TemplateRules.WhyNotSend(User([Roles.HrAdmin]), termination, hr, bulk: false));
        Assert.NotNull(TemplateRules.WhyNotSend(User([Roles.HrAdmin]), termination, hr, bulk: true));
        Assert.NotNull(TemplateRules.WhyNotSend(User([Roles.HrExecutive]), termination, hr, bulk: false));
        Assert.False(TemplateRules.CanSee(User([Roles.HrExecutive]), termination));
    }

    [Fact]
    public void Email_templates_matrix_HR_use_in_their_countries_hiring_managers_interview_templates_on_their_requisitions()
    {
        var interview = EmailTemplate.Create(Content(), Admin, Now);
        var rejection = EmailTemplate.Create(Content(category: TemplateCategory.Rejection) with { Name = "Rejection" }, Admin, Now);
        var own = new SendFacts("PK", IsHr: false, IsHiringManager: true);

        Assert.Null(TemplateRules.WhyNotSend(User([Roles.HrExecutive]), rejection, new SendFacts("PK", true, false), bulk: true));
        Assert.NotNull(TemplateRules.WhyNotSend(User([Roles.HrExecutive]), rejection, new SendFacts("US", false, false), bulk: false));
        Assert.Null(TemplateRules.WhyNotSend(User([Roles.HiringManager]), interview, own, bulk: false));
        Assert.NotNull(TemplateRules.WhyNotSend(User([Roles.HiringManager]), rejection, own, bulk: false));
        Assert.NotNull(TemplateRules.WhyNotSend(User([Roles.HiringManager]), interview, new SendFacts("PK", false, false), bulk: false));
        Assert.NotNull(TemplateRules.WhyNotSend(User([Roles.LineManager, Roles.Employee]), interview, own, bulk: false));
        Assert.True(TemplateRules.CanSee(User([Roles.HiringManager]), interview));
        Assert.False(TemplateRules.CanSee(User([Roles.HiringManager]), rejection));
        Assert.True(TemplateRules.CanManage(User([Roles.HrAdmin])));
        Assert.False(TemplateRules.CanManage(User([Roles.HrExecutive])));
    }

    [Fact]
    public void Email_HTML_is_sanitised_to_an_allowlist_and_keeps_merge_fields_in_links()
    {
        var clean = EmailHtml.Sanitize(
            "<p onclick=\"x()\" style=\"color:red\">Hi <strong>{{first_name}}</strong><script>alert(1)</script></p>" +
            "<a href=\"javascript:alert(1)\">bad</a> <a href=\"{{requisition_link}}\">Review</a> <a href=\"https://example.com/a?b=1\">ok</a>" +
            "<img src=\"https://tracker.example/x.png\"><iframe src=\"https://evil\"></iframe>");

        Assert.Equal(
            "<p>Hi <strong>{{first_name}}</strong></p><a>bad</a> <a href=\"{{requisition_link}}\">Review</a> <a href=\"https://example.com/a?b=1\">ok</a>",
            clean);
    }

    [Fact]
    public void ET_starter_templates_cover_the_spec_and_are_valid()
    {
        Assert.Equal(13, StarterTemplates.All.Count);
        Assert.Equal(StarterTemplates.All.Count, StarterTemplates.All.Select(s => s.Key).Distinct().Count());
        foreach (var starter in StarterTemplates.All)
        {
            var template = EmailTemplate.Create(starter.Content with { BodyHtml = EmailHtml.Sanitize(starter.Content.BodyHtml) }, Admin, Now, starter.Trigger, starter.Key);
            Assert.Equal(starter.Content.BodyHtml, template.BodyHtml);
        }

        Assert.Single(StarterTemplates.All, s => s.Content.Category == TemplateCategory.Termination);
        Assert.All(StarterTemplates.All.Where(s => s.Content.Name.Contains("PIP") || s.Content.Name.Contains("Probation")), s => Assert.True(s.Content.CcManager));
    }

    [Fact]
    public void Interview_times_in_emails_are_split_into_local_date_and_time_with_zone()
    {
        var moment = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal("Mon 2 Nov 2026", LocalCalendar.FormatDate(moment, "Asia/Karachi"));
        Assert.Equal("15:00 Asia/Karachi (UTC+05:00)", LocalCalendar.FormatTime(moment, "Asia/Karachi"));
        Assert.Equal("5 Jan 2027", LocalCalendar.FormatDate(new DateOnly(2027, 1, 5)));
    }
}
