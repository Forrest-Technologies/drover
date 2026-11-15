namespace Drover.Engine.Tests.Files;

/// <summary>A file store in memory, shared by every host of a world, holding keys to the same rule as any store.</summary>
public sealed class MemoryFileStore : IFileStore
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, byte[]> files = new(StringComparer.Ordinal);

    #endregion

    #region Properties

    public int Count => files.Count;

    #endregion

    #region Interface Implementations

    public async Task Save(Guid spaceId, string storageKey, Stream content, CancellationToken cancellationToken)
    {
        FileStorageKeys.Require(spaceId, storageKey);
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        files[storageKey] = copy.ToArray();
    }

    public Task<Stream?> Open(Guid spaceId, string storageKey, CancellationToken cancellationToken)
    {
        FileStorageKeys.Require(spaceId, storageKey);
        return Task.FromResult<Stream?>(files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
    }

    public Task Delete(Guid spaceId, string storageKey, CancellationToken cancellationToken)
    {
        FileStorageKeys.Require(spaceId, storageKey);
        files.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }

    #endregion
}
