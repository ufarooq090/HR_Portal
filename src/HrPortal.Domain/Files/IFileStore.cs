namespace HrPortal.Domain.Files;

/// <summary>Stores documents (resumes, letters, attachments). Azure Blob Storage; Azurite locally.</summary>
public interface IFileStore
{
    /// <returns>Opaque storage key. Never derived from sensitive data.</returns>
    Task<string> SaveAsync(string container, string fileName, string contentType, Stream content, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string container, string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string container, string key, CancellationToken cancellationToken = default);
}
