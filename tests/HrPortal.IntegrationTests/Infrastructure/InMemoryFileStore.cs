using System.Collections.Concurrent;
using HrPortal.Domain.Files;

namespace HrPortal.IntegrationTests.Infrastructure;

/// <summary>Fake <see cref="IFileStore"/> so integration tests need no Azurite.</summary>
public sealed class InMemoryFileStore : IFileStore
{
    private readonly ConcurrentDictionary<(string Container, string Key), byte[]> _files = new();

    public bool Contains(string container, string key) => _files.ContainsKey((container, key));

    public async Task<string> SaveAsync(string container, string fileName, string contentType, Stream content, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var key = Guid.NewGuid().ToString("N");
        _files[(container, key)] = buffer.ToArray();
        return key;
    }

    public Task<Stream> OpenReadAsync(string container, string key, CancellationToken cancellationToken = default) =>
        _files.TryGetValue((container, key), out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
            : throw new FileNotFoundException(key);

    public Task DeleteAsync(string container, string key, CancellationToken cancellationToken = default)
    {
        _files.TryRemove((container, key), out _);
        return Task.CompletedTask;
    }
}
