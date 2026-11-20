namespace Drover.Engine.Tests.Files;

/// <summary>A file store in memory, shared by every host of a world, holding keys to the same rule as any store.</summary>
public sealed class MemoryFileStore : IFileStore
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, byte[]> files = new(StringComparer.Ordinal);

    private int saved;

    private int opened;

    #endregion

    #region Properties

    public int Count => files.Count;

    /// <summary>How many saves took their whole content, including any removed since.</summary>
    public int Saved => saved;

    /// <summary>
    /// Awaited at the start of <see cref="Open"/>, so a test can pause after a caller has read its rows and before it
    /// copies the bytes. Null does nothing.
    /// </summary>
    public Func<CancellationToken, Task>? BeforeOpen { get; set; }

    /// <summary>How many times <see cref="Open"/> read stored content.</summary>
    public int Opened => opened;

    #endregion

    #region Interface Implementations

    public async Task Save(Guid spaceId, string storageKey, Stream content, CancellationToken cancellationToken)
    {
        FileStorageKeys.Require(spaceId, storageKey);
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        files[storageKey] = copy.ToArray();
        Interlocked.Increment(ref saved);
    }

    public async Task<Stream?> Open(Guid spaceId, string storageKey, CancellationToken cancellationToken)
    {
        FileStorageKeys.Require(spaceId, storageKey);

        if (BeforeOpen is { } before)
        {
            await before(cancellationToken);
        }

        Interlocked.Increment(ref opened);
        return files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes, writable: false) : null;
    }

    public Task Delete(Guid spaceId, string storageKey, CancellationToken cancellationToken)
    {
        FileStorageKeys.Require(spaceId, storageKey);
        files.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }

    #endregion
}
