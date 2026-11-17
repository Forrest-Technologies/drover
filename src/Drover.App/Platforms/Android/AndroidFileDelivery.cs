namespace Drover.App.Platforms.Android;

public sealed class AndroidFileDelivery(DroverApi api, DocumentDestination documents, ILogger<AndroidFileDelivery> logger) : IFileDelivery
{
    #region Interface Implementations

    public async Task<bool> Save(SharedFile file, CancellationToken cancellationToken = default)
    {
        var staged = await Download(file, cancellationToken);
        try
        {
            if (await documents.Choose(staged.Name, MimeType(file), cancellationToken) is not { } destination)
            {
                return false;
            }

            var resolver = AndroidApplication.Context.ContentResolver!;
            try
            {
                // Off the interface's thread: the place chosen may be a cloud provider that is slow to open or close.
                await Task.Run(() => Copy(staged, resolver, destination, cancellationToken), cancellationToken);
                return true;
            }
            catch (Exception exception)
            {
                Discard(resolver, destination);
                throw AsFileFailure(exception) ?? exception;
            }
        }
        finally
        {
            Remove(staged.Directory!);
        }
    }

    public async Task Share(SharedFile file, CancellationToken cancellationToken = default)
    {
        var staged = await Download(file, cancellationToken);
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => global::Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(
                new ShareFileRequest { Title = "Share file", File = new ShareFile(staged.FullName, MimeType(file)) }));
        }
        catch (Exception exception)
        {
            Remove(staged.Directory!);
            throw AsFileFailure(exception) ?? exception;
        }
    }

    #endregion

    #region Private Methods

    private async Task<FileInfo> Download(SharedFile file, CancellationToken cancellationToken)
    {
        using var response = await api.OpenConversationFile(file.Href, cancellationToken);
        var root = Path.Combine(FileSystem.CacheDirectory, "sharing-root");
        Directory.CreateDirectory(root);
        ClearOldShares(root);
        var directory = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N")));
        var path = Path.Combine(directory.FullName, SafeName(file.Name));
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(path);
            await source.CopyToAsync(output, cancellationToken);
            return new FileInfo(path);
        }
        catch
        {
            directory.Delete(recursive: true);
            throw;
        }
    }

    private static async Task Copy(FileInfo staged, ContentResolver resolver, AndroidUri destination, CancellationToken cancellationToken)
    {
        await using var source = staged.OpenRead();
        await using var target = resolver.OpenOutputStream(destination, "wt")
            ?? throw new IOException("Android did not open the location you chose.");
        await source.CopyToAsync(target, cancellationToken);
    }

    // Android's own failures are Java exceptions. The file card knows a failed save or share as an IOException.
    private static IOException? AsFileFailure(Exception exception) =>
        exception is Java.Lang.Exception or UnauthorizedAccessException
            ? new IOException("Android could not write or share the file.", exception)
            : null;

    // A save that failed must not leave an empty or cut-off document where the person chose to put the file.
    private void Discard(ContentResolver resolver, AndroidUri destination)
    {
        try
        {
            global::Android.Provider.DocumentsContract.DeleteDocument(resolver, destination);
        }
        catch (Exception exception) when (exception is Java.Lang.Exception or IOException)
        {
            logger.LogWarning(exception, "A document left by a failed save could not be removed");
        }
    }

    private void Remove(DirectoryInfo folder)
    {
        try
        {
            folder.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // It is in the app's own cache, and a later download clears folders left behind.
            logger.LogWarning(exception, "A staged copy of a conversation file could not be removed");
        }
    }

    private static string SafeName(string name) =>
        Path.GetFileName(name.Replace('\\', '/')) is { Length: > 0 } filename && filename is not ("." or "..")
            ? filename
            : "Drover file";

    private static string MimeType(SharedFile file) =>
        MediaTypeHeaderValue.TryParse(file.ContentType, out var mediaType) && mediaType.MediaType is { } type
            ? type
            : "application/octet-stream";

    private void ClearOldShares(string root)
    {
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (Guid.TryParseExact(Path.GetFileName(folder), "N", out _)
                && Directory.GetCreationTimeUtc(folder) < DateTime.UtcNow.AddDays(-1))
            {
                Remove(new DirectoryInfo(folder));
            }
        }
    }

    #endregion
}
