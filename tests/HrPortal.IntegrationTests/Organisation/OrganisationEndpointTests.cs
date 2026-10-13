using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Security;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Organisation.OrganisationEndpoints;

namespace HrPortal.IntegrationTests.Organisation;

[Collection(ApiCollection.Name)]
public class OrganisationEndpointTests(HrPortalApiFactory factory)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid().ToString("N")[..8]}".ToUpperInvariant();

    [Theory]
    [MemberData(nameof(SeedData.DevUsersData), MemberType = typeof(SeedData))]
    public async Task MC_01_any_signed_in_user_can_read_organisation_structure(string devUser)
    {
        var client = factory.CreateClientAs(devUser);

        foreach (var path in new[] { "legal-entities", "locations", "departments", "grades" })
        {
            var response = await client.GetAsync($"/api/organisation/{path}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory]
    [MemberData(nameof(SeedData.DevUsersExceptHrAdmins), MemberType = typeof(SeedData))]
    public async Task NFR_02_only_HR_admins_can_change_organisation_structure(string devUser)
    {
        var client = factory.CreateClientAs(devUser);
        var existing = await factory.WithDbAsync(async db => new
        {
            LegalEntity = await db.LegalEntities.Select(e => e.Id).FirstAsync(),
            Location = await db.OfficeLocations.Select(e => e.Id).FirstAsync(),
            Department = await db.Departments.Select(e => e.Id).FirstAsync(),
            Grade = await db.Grades.Select(e => e.Id).FirstAsync(),
        });

        HttpResponseMessage[] responses =
        [
            await client.PostAsJsonAsync("/api/organisation/legal-entities", new CreateLegalEntityRequest("PK", Unique("LE"))),
            await client.PutAsJsonAsync($"/api/organisation/legal-entities/{existing.LegalEntity}", new UpdateLegalEntityRequest("x", true)),
            await client.DeleteAsync($"/api/organisation/legal-entities/{existing.LegalEntity}"),
            await client.PostAsJsonAsync("/api/organisation/locations", new CreateOfficeLocationRequest("PK", Unique("LOC"), null, null)),
            await client.PutAsJsonAsync($"/api/organisation/locations/{existing.Location}", new UpdateOfficeLocationRequest("x", null, null, true)),
            await client.DeleteAsync($"/api/organisation/locations/{existing.Location}"),
            await client.PostAsJsonAsync("/api/organisation/departments", new CreateDepartmentRequest(Unique("D"), "x")),
            await client.PutAsJsonAsync($"/api/organisation/departments/{existing.Department}", new UpdateDepartmentRequest("X", "x", true)),
            await client.DeleteAsync($"/api/organisation/departments/{existing.Department}"),
            await client.PostAsJsonAsync("/api/organisation/grades", new CreateGradeRequest("PK", Unique("G"), "x", 1, null, null)),
            await client.PutAsJsonAsync($"/api/organisation/grades/{existing.Grade}", new UpdateGradeRequest("X", "x", 1, null, null, true)),
            await client.DeleteAsync($"/api/organisation/grades/{existing.Grade}"),
        ];

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    [Fact]
    public async Task MC_01_HR_admin_manages_legal_entities_and_changes_are_audited()
    {
        var client = factory.CreateClientAs("dev-pk-hradmin");
        var name = $"Test Holdings {Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync("/api/organisation/legal-entities", new CreateLegalEntityRequest("pk", name));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var entity = (await created.Content.ReadFromJsonAsync<LegalEntityResponse>())!;
        Assert.Equal("PK", entity.CountryCode);
        Assert.True(entity.IsActive);

        var updated = await client.PutAsJsonAsync($"/api/organisation/legal-entities/{entity.Id}", new UpdateLegalEntityRequest(name + " Ltd", false));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.False((await updated.Content.ReadFromJsonAsync<LegalEntityResponse>())!.IsActive);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/organisation/legal-entities/{entity.Id}")).StatusCode);

        var actions = await factory.WithDbAsync(db => db.AuditEntries
            .Where(a => a.EntityType == "LegalEntity" && a.EntityId == entity.Id.ToString())
            .Select(a => a.Action).ToListAsync());
        Assert.Equal([AuditAction.Created, AuditAction.Updated, AuditAction.Deleted], actions.Order());
    }

    [Fact]
    public async Task MC_01_office_location_inherits_or_overrides_the_country_time_zone()
    {
        var client = factory.CreateClientAs("dev-us-hradmin");

        var inherits = await (await client.PostAsJsonAsync("/api/organisation/locations",
            new CreateOfficeLocationRequest("US", Unique("Office "), "Austin", null))).Content.ReadFromJsonAsync<OfficeLocationResponse>();
        var overrides = await (await client.PostAsJsonAsync("/api/organisation/locations",
            new CreateOfficeLocationRequest("US", Unique("Office "), "Denver", "America/Denver"))).Content.ReadFromJsonAsync<OfficeLocationResponse>();

        Assert.Null(inherits!.TimeZone);
        Assert.Equal("America/New_York", inherits.EffectiveTimeZone);
        Assert.Equal("America/Denver", overrides!.EffectiveTimeZone);
    }

    [Fact]
    public async Task MC_01_grade_reports_effective_terms_from_country_defaults_and_overrides()
    {
        var client = factory.CreateClientAs("dev-pk-hradmin");

        var response = await client.PostAsJsonAsync("/api/organisation/grades", new CreateGradeRequest("PK", Unique("G"), "Director", 9, null, 90));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var grade = (await response.Content.ReadFromJsonAsync<GradeResponse>())!;
        Assert.Equal(3, grade.EffectiveProbationMonths);
        Assert.Equal(90, grade.EffectiveNoticePeriodDays);
        Assert.Null(grade.ProbationMonths);
    }

    [Fact]
    public async Task Departments_can_be_created_renamed_and_deactivated()
    {
        var client = factory.CreateClientAs("dev-us-hradmin");
        var code = Unique("D");

        var department = await (await client.PostAsJsonAsync("/api/organisation/departments", new CreateDepartmentRequest(code.ToLowerInvariant(), "Legal")))
            .Content.ReadFromJsonAsync<DepartmentResponse>();
        Assert.Equal(code, department!.Code);

        var renamed = await (await client.PutAsJsonAsync($"/api/organisation/departments/{department.Id}", new UpdateDepartmentRequest(code, "Legal & Compliance", false)))
            .Content.ReadFromJsonAsync<DepartmentResponse>();
        Assert.Equal("Legal & Compliance", renamed!.Name);
        Assert.False(renamed.IsActive);
    }

    [Fact]
    public async Task Duplicate_codes_return_409()
    {
        var client = factory.CreateClientAs("dev-pk-hradmin");

        var response = await client.PostAsJsonAsync("/api/organisation/departments", new CreateDepartmentRequest("ENG", "Engineering 2"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Items_in_use_cannot_be_deleted()
    {
        var engineering = await factory.WithDbAsync(db => db.Departments.Where(d => d.Code == "ENG").Select(d => d.Id).SingleAsync());

        var response = await factory.CreateClientAs("dev-pk-hradmin").DeleteAsync($"/api/organisation/departments/{engineering}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_country_and_invalid_input_return_400()
    {
        var client = factory.CreateClientAs("dev-pk-hradmin");

        var unknownCountry = await client.PostAsJsonAsync("/api/organisation/legal-entities", new CreateLegalEntityRequest("GB", "UK Ltd"));
        var badTimeZone = await client.PostAsJsonAsync("/api/organisation/locations", new CreateOfficeLocationRequest("PK", "Somewhere", null, "Lahore"));

        Assert.Equal(HttpStatusCode.BadRequest, unknownCountry.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badTimeZone.StatusCode);
        var problem = await badTimeZone.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("timeZone", out _));
    }

    [Fact]
    public async Task MC_03_country_limited_HR_admin_cannot_change_another_countrys_structure()
    {
        await using var _ = await factory.SetHrAccessAsync("dev-pk-hradmin", CountryScope.Of(["PK"]));
        var client = factory.CreateClientAs("dev-pk-hradmin");
        var usGrade = await factory.WithDbAsync(db => db.Grades.Where(g => g.CountryCode == "US").Select(g => g.Id).FirstAsync());

        var createUs = await client.PostAsJsonAsync("/api/organisation/legal-entities", new CreateLegalEntityRequest("US", Unique("LE")));
        var editUs = await client.PutAsJsonAsync($"/api/organisation/grades/{usGrade}", new UpdateGradeRequest("G9", "x", 1, null, null, true));
        var createPk = await client.PostAsJsonAsync("/api/organisation/legal-entities", new CreateLegalEntityRequest("PK", Unique("LE")));

        Assert.Equal(HttpStatusCode.Forbidden, createUs.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, editUs.StatusCode);
        Assert.Equal(HttpStatusCode.Created, createPk.StatusCode);
    }
}
