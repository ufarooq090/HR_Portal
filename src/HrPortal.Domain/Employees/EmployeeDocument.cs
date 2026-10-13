using System.Text.Json.Serialization;
using HrPortal.Domain.Common;
using HrPortal.Domain.Files;

namespace HrPortal.Domain.Employees;

[JsonConverter(typeof(JsonStringEnumConverter<DocumentCategory>))]
public enum DocumentCategory
{
    IdProof,
    Certificate,
    Contract,
    SignedLetter,
    Other,
}

/// <summary>EMP-06: a file stored against an employee record via <see cref="IFileStore"/>.</summary>
public sealed class EmployeeDocument : Entity
{
    public const string Container = "employee-documents";

    private EmployeeDocument()
    {
    }

    public EmployeeDocument(
        Guid employeeId, DocumentCategory category, UploadedFile file, string storageKey, DateOnly? expiryDate, DateTimeOffset uploadedAt)
    {
        EmployeeId = employeeId;
        Category = category;
        FileName = file.FileName;
        ContentType = file.ContentType;
        SizeBytes = file.SizeBytes;
        StorageKey = storageKey;
        ExpiryDate = expiryDate;
        UploadedAtUtc = uploadedAt.ToUniversalTime();
    }

    public Guid EmployeeId { get; private set; }

    public DocumentCategory Category { get; private set; }

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    /// <summary>Opaque key in <see cref="Container"/>; never exposed to clients.</summary>
    public string StorageKey { get; private set; } = null!;

    /// <summary>For contracts, visas and certificates; expiry alerts come later (SUP-01).</summary>
    public DateOnly? ExpiryDate { get; private set; }

    public DateTimeOffset UploadedAtUtc { get; private set; }
}
