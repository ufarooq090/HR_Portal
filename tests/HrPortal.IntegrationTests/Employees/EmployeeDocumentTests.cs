using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HrPortal.Domain.Audit;
using HrPortal.Domain.Employees;
using HrPortal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static HrPortal.Api.Employees.EmployeeDocumentEndpoints;

namespace HrPortal.IntegrationTests.Employees;

/// <summary>EMP-06 and NFR-06: documents stored through IFileStore, visible to HR and the employee only.</summary>
[Collection(ApiCollection.Name)]
public class EmployeeDocumentTests(HrPortalApiFactory factory)
{
    private static readonly byte[] Pdf = "%PDF-1.7\n% test document\n"u8.ToArray();

    private static MultipartFormDataContent Form(byte[] content, string fileName, DocumentCategory category = DocumentCategory.Contract, string? expiry = null)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent { { file, "file", fileName }, { new StringContent(category.ToString()), "category" } };
        if (expiry is not null)
        {
            form.Add(new StringContent(expiry), "expiryDate");
        }

        return form;
    }

    private async Task<(Guid EmployeeId, DocumentDto Document)> UploadAsync(Guid? employeeId = null)
    {
        var id = employeeId ?? (await factory.CreateEmployeeAsync("PK", await factory.EmployeeIdAsync("PK-0005"))).Id;
        var response = await factory.CreateClientAs("dev-pk-hrexecutive").PostAsync($"/api/employees/{id}/documents", Form(Pdf, "contract.pdf", expiry: "2027-06-30"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (id, (await response.Content.ReadFromJsonAsync<DocumentDto>())!);
    }

    [Fact]
    public async Task EMP_06_HR_uploads_a_document_and_downloads_it_back()
    {
        var (employeeId, document) = await UploadAsync();
        var client = factory.CreateClientAs("dev-pk-hradmin");

        var list = await client.GetFromJsonAsync<List<DocumentDto>>($"/api/employees/{employeeId}/documents");
        var download = await client.GetAsync($"/api/employees/{employeeId}/documents/{document.Id}/content");

        var listed = Assert.Single(list!);
        Assert.Equal("contract.pdf", listed.FileName);
        Assert.Equal("application/pdf", listed.ContentType);
        Assert.Equal(new DateOnly(2027, 6, 30), listed.ExpiryDate);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("contract.pdf", download.Content.Headers.ContentDisposition!.FileNameStar ?? download.Content.Headers.ContentDisposition.FileName);
        Assert.True(await factory.WithDbAsync(db => db.AuditEntries.AnyAsync(a =>
            a.Action == AuditAction.Viewed && a.EntityType == "EmployeeDocument" && a.EntityId == document.Id.ToString())));
    }

    [Theory]
    [InlineData("dev-pk-linemanager")]
    [InlineData("dev-pk-hiringmanager")]
    [InlineData("dev-pk-systemadmin")]
    [InlineData("dev-us-hrexecutive")]
    [InlineData("dev-pk-employee")]
    public async Task NFR_02_managers_and_other_countries_cannot_see_documents(string devUser)
    {
        var (employeeId, document) = await UploadAsync();
        var client = factory.CreateClientAs(devUser);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/employees/{employeeId}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/employees/{employeeId}/documents/{document.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/employees/{employeeId}/documents/{document.Id}")).StatusCode);
    }

    [Fact]
    public async Task EMP_06_employee_sees_their_own_documents_but_cannot_upload_or_delete()
    {
        var self = await factory.EmployeeIdAsync("PK-0006");
        var (_, document) = await UploadAsync(self);
        var client = factory.CreateClientAs("dev-pk-employee");

        Assert.Contains((await client.GetFromJsonAsync<List<DocumentDto>>($"/api/employees/{self}/documents"))!, d => d.Id == document.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/employees/{self}/documents/{document.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/employees/{self}/documents", Form(Pdf, "mine.pdf"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/employees/{self}/documents/{document.Id}")).StatusCode);

        await factory.CreateClientAs("dev-pk-hradmin").DeleteAsync($"/api/employees/{self}/documents/{document.Id}");
    }

    [Fact]
    public async Task EMP_06_deleting_removes_the_record_and_the_stored_file()
    {
        var (employeeId, document) = await UploadAsync();
        var key = await factory.WithDbAsync(db => db.EmployeeDocuments.Where(d => d.Id == document.Id).Select(d => d.StorageKey).SingleAsync());

        var response = await factory.CreateClientAs("dev-pk-hradmin").DeleteAsync($"/api/employees/{employeeId}/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(factory.Files.Contains(EmployeeDocument.Container, key));
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClientAs("dev-pk-hradmin").GetAsync($"/api/employees/{employeeId}/documents/{document.Id}/content")).StatusCode);
    }

    [Theory]
    [InlineData("malware.exe", "MZ-not-allowed")]
    [InlineData("disguised.pdf", "MZ-really-an-exe")]
    [InlineData("empty.pdf", "")]
    public async Task NFR_06_disallowed_or_disguised_files_are_rejected(string fileName, string content)
    {
        var employee = await factory.CreateEmployeeAsync("PK");

        var response = await factory.CreateClientAs("dev-pk-hradmin").PostAsync(
            $"/api/employees/{employee.Id}/documents", Form(System.Text.Encoding.ASCII.GetBytes(content), fileName));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await factory.CreateClientAs("dev-pk-hradmin").GetFromJsonAsync<List<DocumentDto>>($"/api/employees/{employee.Id}/documents"))!);
    }

    [Fact]
    public async Task NFR_06_files_over_10_MB_are_rejected()
    {
        var employee = await factory.CreateEmployeeAsync("PK");
        var big = new byte[(10 * 1024 * 1024) + 1];
        Pdf.CopyTo(big, 0);

        var response = await factory.CreateClientAs("dev-pk-hradmin").PostAsync($"/api/employees/{employee.Id}/documents", Form(big, "big.pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
