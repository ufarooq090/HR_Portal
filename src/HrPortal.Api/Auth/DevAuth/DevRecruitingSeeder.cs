using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Files;
using HrPortal.Domain.Recruiting;
using HrPortal.Domain.Requisitions;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace HrPortal.Api.Auth.DevAuth;

/// <summary>
/// Fictional recruiting data so the Candidate Tracker and Resume Bank can be explored locally: open requisitions
/// in both countries with candidates at every stage, interviews with scorecards, searchable CVs, and Resume Bank
/// candidates who never applied (one blacklisted). Development only: never in Test (tests seed their own data)
/// or any deployed environment. Runs once; candidates are recognised by their @candidates.dev.test email.
/// </summary>
public static class DevRecruitingSeeder
{
    private const string EmailDomain = "@candidates.dev.test";

    private sealed record Candidate(
        string GivenName,
        string FamilyName,
        string Employer,
        string Location,
        decimal Experience,
        int Notice,
        decimal Ctc,
        string[] Skills,
        string CvSummary,
        PipelineStage? Stage = null,
        int? Round = null,
        bool PastInterview = false,
        bool UpcomingInterview = false);

    public static async Task SeedAsync(HrPortalDbContext db, IFileStore files, IHostEnvironment environment, CancellationToken ct = default)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Recruiting sample data is only seeded in Development.");
        }

        if (await db.People.AnyAsync(p => p.PersonalEmail != null && p.PersonalEmail.EndsWith(EmailDomain), ct))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var hr = new Actor("dev-pk-hradmin", "Dev HR Admin (PK)", null);
        var rejection = await db.RejectionReasons.OrderBy(r => r.SortOrder).FirstAsync(ct);
        var currencies = await db.CountrySettings.Select(c => c.CurrencyCode).ToListAsync(ct);
        var ceo = await db.Employees.SingleAsync(e => e.UserObjectId == DevUsers.Ceo.Id, ct);

        async Task<Employee> UserAsync(string id) => await db.Employees.SingleAsync(e => e.UserObjectId == id, ct);

        async Task<Requisition> RequisitionAsync(string country, string title, string office, string department, string description, decimal min, decimal max)
        {
            var settings = await db.CountrySettings.SingleAsync(c => c.CountryCode == country, ct);
            var manager = await UserAsync($"dev-{country.ToLowerInvariant()}-hiringmanager");
            var requisition = Requisition.Create(
                await db.NextRequisitionNumberAsync(ct),
                new RequisitionDetails(
                    title,
                    await db.Departments.SingleAsync(d => d.Code == department, ct),
                    await db.OfficeLocations.SingleAsync(o => o.Name == office, ct),
                    2,
                    new Money(min, settings.CurrencyCode),
                    new Money(max, settings.CurrencyCode),
                    RequisitionReason.NewPosition,
                    null,
                    today.AddMonths(3),
                    description,
                    manager),
                settings,
                manager.Id,
                new Actor($"dev-{country.ToLowerInvariant()}-hiringmanager", $"Dev Hiring Manager ({country})", manager.Id),
                now.AddDays(-30));
            requisition.Submit([new ApprovalStep("CEO", ceo.Id)], hr, today, now.AddDays(-29));
            requisition.Decide(ceo.Id, true, "Approved for this quarter.", new Actor(DevUsers.Ceo.Id, DevUsers.Ceo.DisplayName, ceo.Id), now.AddDays(-28));
            db.Requisitions.Add(requisition);
            return requisition;
        }

        async Task<CandidateProfile> PersonAsync(string country, Candidate c, DateTimeOffset at)
        {
            var person = new Person(c.GivenName, c.FamilyName);
            person.UpdateContact(
                $"{c.GivenName}.{c.FamilyName}{EmailDomain}".ToLowerInvariant(),
                country == "PK" ? "+92 321 555 0100" : "+1 646 555 0100");
            db.People.Add(person);
            var profile = new CandidateProfile(person.Id, country, at);
            var currency = country == "PK" ? "PKR" : "USD";
            profile.Update(c.Employer, c.Notice, new Money(c.Ctc, currency), new Money(Math.Round(c.Ctc * 1.25m), currency), currencies.ToHashSet());
            profile.UpdateResumeDetails(country, c.Location, c.Experience, c.Skills, c.Experience >= 6 ? ["senior"] : []);
            db.CandidateProfiles.Add(profile);

            var text = $"{c.GivenName} {c.FamilyName}\n{c.Location}\n\nSummary\n{c.CvSummary}\n\nExperience\n{c.Experience} years, currently at {c.Employer}.\n\nSkills\n{string.Join(", ", c.Skills)}";
            var pdf = CvPdf(text);
            var fileName = $"{c.GivenName}-{c.FamilyName}-CV.pdf";
            var key = await files.SaveAsync(CandidateCv.Container, fileName, "application/pdf", new MemoryStream(pdf), ct);
            db.CandidateCvs.Add(new CandidateCv(person.Id, new UploadedFile(fileName, "application/pdf", pdf.Length), key, hr.Name, at, text));
            return profile;
        }

        async Task PipelineAsync(Requisition requisition, Candidate[] candidates)
        {
            var country = requisition.CountryCode;
            var cc = country.ToLowerInvariant();
            var panel = new[] { await UserAsync($"dev-{cc}-interviewer"), await UserAsync($"dev-{cc}-hiringmanager") };
            var hrActor = new Actor($"dev-{cc}-hradmin", $"Dev HR Admin ({country})", null);
            for (var i = 0; i < candidates.Length; i++)
            {
                var c = candidates[i];
                var at = now.AddDays(-25 + i);
                var profile = await PersonAsync(country, c, at);
                var application = Application.Create(profile.PersonId, requisition, i % 3 == 0 ? CandidateSource.Referral : CandidateSource.JobBoard, hrActor, at);
                db.Applications.Add(application);
                profile.MarkContacted(at);

                void Move(PipelineStage stage, int? round, DateTimeOffset when)
                {
                    application.MoveTo(
                        stage, round, stage == PipelineStage.Rejected ? rejection : null,
                        stage == PipelineStage.Rejected ? "Strong on theory, little hands-on experience." : null,
                        requisition.InterviewRounds, hrActor, when);
                    profile.ApplyAutomaticStatus(
                        ResumeBankStatusRules.FromPipeline(stage), StatusChangeSource.Pipeline,
                        $"{requisition.Number} {requisition.Title}: {Application.Label(stage)}", hrActor, when);
                    profile.MarkContacted(when);
                }

                if (c.Stage is not { } target)
                {
                    continue;
                }

                Move(PipelineStage.Screening, null, at.AddDays(1));
                if (c.PastInterview || target is PipelineStage.Offer or PipelineStage.OfferDeclined)
                {
                    Move(PipelineStage.Interview, 1, at.AddDays(2));
                    var interview = Interview.Schedule(
                        application, 1, requisition.InterviewRounds, AtHour(at.AddDays(4), 6), 60, InterviewMode.Teams, null, panel, requisition.ScorecardCriteria);
                    db.Interviews.Add(interview);
                    foreach (var interviewer in panel)
                    {
                        var hire = target != PipelineStage.Rejected;
                        db.Scorecards.Add(Scorecard.Submit(
                            interview,
                            interviewer.Id,
                            interview.Criteria.Select(criterion => new CriterionRating(criterion, hire ? 4 : 2, null)).ToList(),
                            hire ? Recommendation.Hire : Recommendation.NoHire,
                            hire ? "Solid fundamentals and clear communication." : "Not ready for this level yet.",
                            at.AddDays(4).AddHours(3)));
                    }
                }

                if (target == PipelineStage.Interview && c.Round is { } round && round != application.InterviewRound)
                {
                    Move(PipelineStage.Interview, round, at.AddDays(5));
                }

                if (target is PipelineStage.Offer or PipelineStage.OfferDeclined)
                {
                    Move(PipelineStage.Offer, null, at.AddDays(6));
                }

                if (target is not (PipelineStage.Screening or PipelineStage.Interview or PipelineStage.Offer))
                {
                    Move(target, null, at.AddDays(7));
                }

                if (c.UpcomingInterview)
                {
                    db.Interviews.Add(Interview.Schedule(
                        application, application.InterviewRound ?? 1, requisition.InterviewRounds, AtHour(now.AddDays(3 + i % 4), 7), 45,
                        InterviewMode.Teams, null, panel, requisition.ScorecardCriteria));
                }
            }
        }

        var dotnet = await RequisitionAsync("PK", "Senior .NET Engineer", "Lahore Office", "ENG",
            "Build and run our ASP.NET Core services and PostgreSQL databases.", 3_600_000m, 5_400_000m);
        var hrGeneralist = await RequisitionAsync("PK", "HR Generalist", "Karachi Office", "HR",
            "Own onboarding, employee records and HR operations for the Karachi office.", 1_500_000m, 2_400_000m);
        var frontend = await RequisitionAsync("US", "Frontend Engineer (React)", "US Office", "ENG",
            "Build accessible React and TypeScript interfaces for our internal products.", 110_000m, 145_000m);

        await PipelineAsync(dotnet,
        [
            new("Ayesha", "Khan", "Systems Ltd", "Lahore", 4, 30, 3_000_000m, ["c#", ".net", "sql"], "Backend developer building ASP.NET Core APIs with Entity Framework."),
            new("Bilal", "Ahmed", "Netsol Technologies", "Lahore", 6, 60, 4_200_000m, ["c#", ".net", "azure"], "Designed microservices on Azure with Service Bus and Kubernetes.", PipelineStage.Screening),
            new("Sana", "Malik", "Arbisoft", "Lahore", 5, 30, 3_800_000m, ["c#", "postgresql", "react"], "Full-stack engineer: PostgreSQL performance tuning and React dashboards.", PipelineStage.Interview, 1, UpcomingInterview: true),
            new("Hamza", "Qureshi", "10Pearls", "Karachi", 7, 60, 4_800_000m, ["c#", ".net", "postgresql", "docker"], "Led migration of a monolith to .NET 8 with PostgreSQL and Docker.", PipelineStage.Interview, 2, PastInterview: true, UpcomingInterview: true),
            new("Fatima", "Raza", "Afiniti", "Islamabad", 8, 90, 5_200_000m, ["c#", ".net", "azure", "sql"], "Principal engineer; distributed systems, Azure Functions and SQL Server.", PipelineStage.Offer),
            new("Usman", "Tariq", "Freelance", "Lahore", 2, 0, 1_800_000m, ["c#", "javascript"], "Junior developer with ASP.NET MVC side projects.", PipelineStage.Rejected, PastInterview: true),
            new("Zainab", "Hussain", "Confiz", "Lahore", 5, 30, 3_500_000m, ["c#", "angular"], "Built Angular front ends on top of .NET Web API services.", PipelineStage.NoShow),
        ]);
        await PipelineAsync(hrGeneralist,
        [
            new("Maria", "Siddiqui", "K-Electric", "Karachi", 5, 30, 1_900_000m, ["hr operations", "onboarding", "payroll"], "HR generalist handling onboarding, payroll inputs and employee relations."),
            new("Ali", "Haider", "Engro", "Karachi", 3, 30, 1_400_000m, ["recruitment", "onboarding"], "Recruitment coordinator running campus hiring drives.", PipelineStage.Screening),
        ]);
        await PipelineAsync(frontend,
        [
            new("Emily", "Carter", "Acme Health", "Brooklyn, NY", 4, 14, 105_000m, ["react", "typescript", "css"], "Frontend engineer building React and TypeScript design systems.", PipelineStage.Screening),
            new("Jordan", "Lee", "Brightline", "Jersey City, NJ", 6, 14, 125_000m, ["react", "typescript", "accessibility"], "Accessibility specialist; WCAG audits and React component libraries.", PipelineStage.Interview, 2, PastInterview: true, UpcomingInterview: true),
            new("Michael", "Brooks", "Northwind", "New York, NY", 8, 30, 150_000m, ["react", "graphql", "node.js"], "Staff engineer leading GraphQL and React migration.", PipelineStage.OfferDeclined),
            new("Priya", "Patel", "Contoso", "Remote", 3, 14, 95_000m, ["vue", "javascript"], "Vue.js developer moving to React.", PipelineStage.Rejected),
        ]);

        // Resume Bank candidates from earlier hiring rounds, not in any current pipeline.
        var omar = await PersonAsync("PK", new("Omar", "Farooq", "Teradata", "Lahore", 9, 60, 6_000_000m, ["postgresql", "data engineering", "python"],
            "Data engineer: PostgreSQL, Airflow pipelines and Python ETL."), now.AddDays(-200));
        omar.SetStatus(ResumeBankStatus.Shortlisted, "Strong fit for future data roles.", hr, now.AddDays(-190));
        omar.MarkContacted(now.AddDays(-190));
        var rachel = await PersonAsync("US", new("Rachel", "Green", "Globex", "Boston, MA", 2, 14, 85_000m, ["react", "javascript"],
            "Junior React developer with a bootcamp background."), now.AddDays(-120));
        rachel.SetStatus(ResumeBankStatus.NotShortlisted, "Too junior for the open roles at the time.", new Actor("dev-us-hradmin", "Dev HR Admin (US)", null), now.AddDays(-118));
        rachel.MarkContacted(now.AddDays(-118));
        var kamran = await PersonAsync("PK", new("Kamran", "Javed", "Unknown", "Karachi", 6, 30, 3_000_000m, ["c#", ".net"],
            ".NET developer."), now.AddDays(-300));
        kamran.Blacklist("Submitted forged experience letters in a previous application (verified by HR).", hr, now.AddDays(-290));
        db.CandidateNotes.Add(new CandidateNote(kamran.PersonId, "Previous employer confirmed the letters were not issued by them.", hr, now.AddDays(-291)));
        db.CandidateNotes.Add(new CandidateNote(omar.PersonId, "Interested in data platform roles; open to relocating to Karachi.", hr, now.AddDays(-190)));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>A whole UTC hour on the same day.</summary>
    private static DateTimeOffset AtHour(DateTimeOffset day, int utcHour) => new(day.UtcDateTime.Date.AddHours(utcHour), TimeSpan.Zero);

    private static byte[] CvPdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var y = 790d;
        foreach (var line in text.Split('\n'))
        {
            page.AddText(line, 11, new PdfPoint(50, y), font);
            y -= 16;
        }

        return builder.Build();
    }
}
