namespace Drover.Adapters.LocalFiles;

/// <summary>
/// Files on the server's own disk, under one root: a folder per space, a file per stored file, each named by its id.
/// A key is checked against the space before it becomes a path (<see cref="FileStorageKeys.Require"/>), so a path
/// is only ever built from two ids and can never leave the space's folder. Content is written to a temporary file
/// and moved into place, so a reader never sees half a file. On Unix the folders and files are readable by the
/// server's own user only.
/// </summary>
public sealed class LocalFileStore(string root) : IFileStore
{
    #region Private Fields

    private const UnixFileMode OwnerFolderMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode OwnerFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const int BufferSize = 81920;

    private readonly string root = Path.GetFullPath(root);

    #endregion

    #region Public Methods

    /// <summary>Checks the configured root using the same account and private file mode as an upload.</summary>
    public void CheckWritable()
    {
        CreateFolder(root);
        using var probe = OpenNew(Path.Combine(root, $".{Guid.NewGuid():N}.probe"), FileOptions.DeleteOnClose);
        probe.WriteByte(0);
        probe.Flush();
    }

    #endregion

    #region Interface Implementations

    public async Task Save(Guid spaceId, string storageKey, Stream content, CancellationToken cancellationToken)
    {
        var path = PathOf(spaceId, storageKey);
        var folder = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(folder, $".{Guid.NewGuid():N}.partial");
        var file = OpenTemporary(folder, temporary);
        try
        {
            await CopyToTemporary(content, file, cancellationToken);
            ChangeFile(() => File.Move(temporary, path, overwrite: true));
        }
        finally
        {
            ChangeFile(() => File.Delete(temporary));
        }
    }

    public Task<Stream?> Open(Guid spaceId, string storageKey, CancellationToken cancellationToken)
    {
        var path = PathOf(spaceId, storageKey);
        Stream? content = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize, useAsync: true)
            : null;
        return Task.FromResult(content);
    }

    public Task Delete(Guid spaceId, string storageKey, CancellationToken cancellationToken)
    {
        File.Delete(PathOf(spaceId, storageKey));
        return Task.CompletedTask;
    }

    #endregion

    #region Private Methods

    private string PathOf(Guid spaceId, string storageKey)
    {
        var fileId = FileStorageKeys.Require(spaceId, storageKey);
        return Path.Combine(root, spaceId.ToString("N"), fileId.ToString("N"));
    }

    private static FileStream OpenTemporary(string folder, string temporary)
    {
        try
        {
            CreateFolder(folder);
            return OpenNew(temporary);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new FileStorageUnavailableException(failure);
        }
    }

    private static async Task CopyToTemporary(Stream source, FileStream file, CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new byte[BufferSize];
            var count = 0;
            while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                await WriteFile(() => file.WriteAsync(buffer.AsMemory(0, count), cancellationToken));
            }
        }
        finally
        {
            await WriteFile(file.DisposeAsync);
        }
    }

    private static async ValueTask WriteFile(Func<ValueTask> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new FileStorageUnavailableException(failure);
        }
    }

    private static void ChangeFile(Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new FileStorageUnavailableException(failure);
        }
    }

    private static void CreateFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            return;
        }

        CreateFolderForOwner(folder);
    }

    [UnsupportedOSPlatform("windows")]
    private static void CreateFolderForOwner(string folder)
    {
        if (Path.GetDirectoryName(folder) is { } parent && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent, OwnerFolderMode);
        }

        Directory.CreateDirectory(folder, OwnerFolderMode);
    }

    private static FileStream OpenNew(string path, FileOptions optionsForFile = FileOptions.None)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = BufferSize,
            Options = FileOptions.Asynchronous | optionsForFile,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerFileMode;
        }

        return new FileStream(path, options);
    }

    #endregion
}
