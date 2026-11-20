namespace Drover.Domain.Files;

/// <summary>
/// The bytes of stored files, behind a key. Every call names the space it acts for, and a key that is not one of
/// that space's (<see cref="FileStorageKeys"/>) is refused with <see cref="ArgumentException"/> before anything is
/// touched, so no key, however it was made, can reach another space's file. Who may read a file is decided by its
/// <c>files</c> row, never by its key: callers read the row inside a tenant scope first.
/// </summary>
public interface IFileStore
{
    /// <summary>
    /// Writes the content under the key, replacing what was there. Nothing is left behind when it fails. Reads
    /// <paramref name="content"/> asynchronously with <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/> only.
    /// </summary>
    Task Save(Guid spaceId, string storageKey, Stream content, CancellationToken cancellationToken);

    /// <summary>The content, for the caller to dispose. Null when nothing is stored under the key.</summary>
    Task<Stream?> Open(Guid spaceId, string storageKey, CancellationToken cancellationToken);

    /// <summary>Removes the content. A key with nothing under it is not an error.</summary>
    Task Delete(Guid spaceId, string storageKey, CancellationToken cancellationToken);
}
