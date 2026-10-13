using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HrPortal.Domain.Files;

namespace HrPortal.Infrastructure.Files;

/// <summary>
/// <see cref="IFileStore"/> on Azure Blob Storage (Azurite locally). Containers are private; files are only ever
/// served through the API after an authorisation check. Keys are random, never derived from file content or names.
/// </summary>
public sealed class BlobFileStore(BlobServiceClient client) : IFileStore
{
    public const string ConnectionStringKey = "Storage:ConnectionString";

    public async Task<string> SaveAsync(string container, string fileName, string contentType, Stream content, CancellationToken cancellationToken = default)
    {
        var containerClient = client.GetBlobContainerClient(container);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var key = Guid.CreateVersion7().ToString("N");
        await containerClient.GetBlobClient(key).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
        return key;
    }

    public async Task<Stream> OpenReadAsync(string container, string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await client.GetBlobContainerClient(container).GetBlobClient(key).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            throw new FileNotFoundException($"No file '{key}' in '{container}'.", e);
        }
    }

    public async Task DeleteAsync(string container, string key, CancellationToken cancellationToken = default) =>
        await client.GetBlobContainerClient(container).GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);
}
