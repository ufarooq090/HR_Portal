using HrPortal.Domain.Common;

namespace HrPortal.Domain.Files;

/// <summary>A validated upload: safe file name, content type derived from the extension, size.</summary>
public sealed record UploadedFile(string FileName, string ContentType, long SizeBytes);

/// <summary>
/// NFR-06: PDF, DOC/DOCX, JPG and PNG only, up to 10 MB. The file's first bytes must match its extension, and
/// the content type comes from the extension, never from the client.
/// </summary>
/// <remarks>Malware scanning before storage is not done here; see docs/requirements.md (still open).</remarks>
public static class UploadPolicy
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, (string ContentType, byte[] Signature)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ("application/pdf", "%PDF"u8.ToArray()),
        [".png"] = ("image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        [".jpg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
        [".jpeg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
        [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [0x50, 0x4B, 0x03, 0x04]),
        [".doc"] = ("application/msword", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
    };

    /// <summary>Bytes needed from the start of the file to check its signature.</summary>
    public const int HeaderLength = 8;

    /// <summary>CVs (RB-01): PDF and Word only.</summary>
    public static readonly IReadOnlySet<string> CvExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx" };

    public static UploadedFile Validate(string? fileName, long sizeBytes, ReadOnlySpan<byte> header) =>
        Validate(fileName, sizeBytes, header, null);

    /// <param name="onlyExtensions">Narrows the allowed types further, e.g. <see cref="CvExtensions"/>.</param>
    public static UploadedFile Validate(string? fileName, long sizeBytes, ReadOnlySpan<byte> header, IReadOnlySet<string>? onlyExtensions)
    {
        var name = SafeFileName(fileName);
        var extension = Path.GetExtension(name);
        if (onlyExtensions is not null && !onlyExtensions.Contains(extension))
        {
            throw new DomainValidationException("file", "Upload a PDF or Word document (DOC/DOCX).");
        }

        if (!Allowed.TryGetValue(extension, out var rule))
        {
            throw new DomainValidationException("file", "Upload a PDF, Word document (DOC/DOCX), JPG or PNG.");
        }

        if (sizeBytes <= 0)
        {
            throw new DomainValidationException("file", "The file is empty.");
        }

        if (sizeBytes > MaxBytes)
        {
            throw new DomainValidationException("file", "Files can be at most 10 MB.");
        }

        if (!header.StartsWith(rule.Signature))
        {
            throw new DomainValidationException("file", $"The file's content does not match a {extension.TrimStart('.').ToUpperInvariant()} file.");
        }

        return new UploadedFile(name, rule.ContentType, sizeBytes);
    }

    /// <summary>Strips any path and control characters; keeps a readable name for downloads.</summary>
    public static string SafeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/').Split('/')[^1]);
        name = new string(name.Where(c => !char.IsControl(c) && c is not '"' and not '<' and not '>' and not '|' and not ':' and not '*' and not '?').ToArray()).Trim();
        if (name.Length == 0 || name.StartsWith('.'))
        {
            throw new DomainValidationException("file", "The file needs a name.");
        }

        return name.Length <= 200 ? name : name[..(200 - Path.GetExtension(name).Length)] + Path.GetExtension(name);
    }
}
