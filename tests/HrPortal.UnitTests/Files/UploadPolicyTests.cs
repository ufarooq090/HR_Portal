using HrPortal.Domain.Common;
using HrPortal.Domain.Files;

namespace HrPortal.UnitTests.Files;

public class UploadPolicyTests
{
    private static readonly byte[] Pdf = "%PDF-1.7"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Theory]
    [InlineData("contract.pdf", "application/pdf")]
    [InlineData("CONTRACT.PDF", "application/pdf")]
    public void NFR_06_allowed_file_gets_its_content_type_from_the_extension(string name, string contentType)
    {
        var file = UploadPolicy.Validate(name, 1000, Pdf);

        Assert.Equal(contentType, file.ContentType);
        Assert.Equal(name, file.FileName);
    }

    [Theory]
    [InlineData("script.exe")]
    [InlineData("page.html")]
    [InlineData("noextension")]
    public void NFR_06_other_types_are_rejected(string name)
    {
        Assert.Throws<DomainValidationException>(() => UploadPolicy.Validate(name, 1000, Pdf));
    }

    [Fact]
    public void NFR_06_content_must_match_the_extension()
    {
        var ex = Assert.Throws<DomainValidationException>(() => UploadPolicy.Validate("photo.pdf", 1000, Png));

        Assert.Contains("PDF", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(UploadPolicy.MaxBytes + 1)]
    public void NFR_06_empty_or_over_10_MB_is_rejected(long size)
    {
        Assert.Throws<DomainValidationException>(() => UploadPolicy.Validate("a.pdf", size, Pdf));
    }

    [Theory]
    [InlineData(@"C:\fakepath\cnic.png", "cnic.png")]
    [InlineData("../../etc/passwd.png", "passwd.png")]
    [InlineData("we\"ird<name>.png", "weirdname.png")]
    public void File_names_lose_paths_and_unsafe_characters(string input, string expected)
    {
        Assert.Equal(expected, UploadPolicy.Validate(input, 10, Png).FileName);
    }
}
