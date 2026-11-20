namespace Drover.App.Platforms.Android;

public sealed class AndroidFileDelivery(DroverApi api, DocumentDestination documents, ILogger<AndroidFileDelivery> logger) : IFileDelivery
{
    #region Private Fields

    private const string PendingSave = "drover.save.pending";

    private const string SharedFolder = "sharing-root";

    private readonly SingleSave saves = new();

    #endregion

    #region Interface Implementations

    public async Task<bool> Save(SharedFile file, CancellationToken cancellationToken = default)
    {
        // The turn is held from the picker to the end of the clean-up, since the marker that recovers an interrupted
        // save is one record and a second save would replace it.
        using var turn = saves.TryStart()
            ?? throw new FileDeliveryException("Another file is being saved. Wait for it to finish, then save this one.");

        // Ask where it goes before anything is downloaded: a person who cancels costs no download, and the file then
        // goes straight to the place they chose instead of through the app's cache.
        if (await documents.Choose(SafeName(file.Name), PickerType(file), cancellationToken) is not { } destination)
        {
            return false;
        }

        var resolver = AndroidApplication.Context.ContentResolver!;
        // If Android stops the app while the file is being written, the next start removes what was left.
        Preferences.Default.Set(PendingSave, destination.ToString() ?? string.Empty);
        try
        {
            using var response = await api.OpenConversationFile(file.Href, cancellationToken);
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            // Off the interface's thread: the place chosen may be a cloud provider that is slow to open or close.
            await Task.Run(() => Copy(source, resolver, destination, cancellationToken), cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            DocumentDestination.Remove(destination, logger);
            throw AsFileFailure(exception) ?? exception;
        }
        finally
        {
            Preferences.Default.Remove(PendingSave);
        }
    }

    public async Task Share(SharedFile file, CancellationToken cancellationToken = default)
    {
        var staged = await Download(file, cancellationToken);
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => ShareSheet.Open("Share file", staged.FullName, MimeType(file)));
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
        var root = System.IO.Path.Combine(FileSystem.CacheDirectory, SharedFolder);
        Directory.CreateDirectory(root);
        ClearOldShares(root);
        var directory = Directory.CreateDirectory(System.IO.Path.Combine(root, Guid.NewGuid().ToString("N")));
        var path = System.IO.Path.Combine(directory.FullName, SafeName(file.Name));
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

    private static async Task Copy(Stream source, ContentResolver resolver, AndroidUri destination, CancellationToken cancellationToken)
    {
        await using var target = resolver.OpenOutputStream(destination, "wt")
            ?? throw new FileDeliveryException("Android did not open the place you chose.");
        await source.CopyToAsync(target, cancellationToken);
    }

    // Android's own failures are Java exceptions. The file card knows a failed save or share as an IOException, and
    // shows the reason this gives, which is a fixed sentence: a system error can name a path.
    private static FileDeliveryException? AsFileFailure(Exception exception) => exception switch
    {
        FileDeliveryException => null,
        Java.Lang.SecurityException or UnauthorizedAccessException =>
            new("Android did not allow Drover to write to the place you chose.", exception),
        Java.Lang.Exception => new("Android could not write the file there.", exception),
        _ => null,
    };

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
        System.IO.Path.GetFileName(name.Replace('\\', '/')) is { Length: > 0 } filename && filename is not ("." or "..")
            ? filename
            : "Drover file";

    // What the picker is told the file is follows the file's name, not the type the server stored: where the two
    // disagree the picker can add a second extension to the name.
    private static string PickerType(SharedFile file) =>
        global::Android.Webkit.MimeTypeMap.Singleton?.GetMimeTypeFromExtension(
            System.IO.Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant()) is { Length: > 0 } type
            ? type
            : "application/octet-stream";

    private static string MimeType(SharedFile file) =>
        MediaTypeHeaderValue.TryParse(file.ContentType, out var mediaType) && mediaType.MediaType is { } type
            ? type
            : "application/octet-stream";

    /// <summary>
    /// Takes every copy made for sharing out of the app's cache. A copy has no use once its share sheet is done, and
    /// it holds a conversation's file, so it does not outlast a sign-out or a start of the app.
    /// </summary>
    public static void ClearSharedCopies(ILogger? logger = null)
    {
        try
        {
            var root = System.IO.Path.Combine(FileSystem.CacheDirectory, SharedFolder);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // It is in the app's own cache, and the next start or download tries again.
            logger?.LogWarning(exception, "Staged copies of conversation files could not be cleared");
        }
    }

    /// <summary>Follows a person's session, and clears what was staged for sharing when they sign out.</summary>
    public static void ClearSharedCopiesOnSignOut(SessionState session) =>
        session.Changed += () =>
        {
            if (session.Current is null)
            {
                ClearSharedCopies();
            }
        };

    /// <summary>
    /// Run when the app starts. Clears the copies staged for sharing, and removes the document a save left behind if
    /// Android stopped the app while it was being written, saying so, since the person saw the save begin.
    /// </summary>
    public static void RecoverFromInterruption(ILogger? logger = null)
    {
        ClearSharedCopies(logger);
        if (Preferences.Default.Get(PendingSave, string.Empty) is { Length: > 0 } pending)
        {
            Preferences.Default.Remove(PendingSave);
            if (AndroidUri.Parse(pending) is { } document)
            {
                DocumentDestination.Remove(document, logger);
            }

            DocumentDestination.Tell("A save was interrupted, so the partly saved file was removed. Save it again.");
        }
    }

    private void ClearOldShares(string root)
    {
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (Guid.TryParseExact(System.IO.Path.GetFileName(folder), "N", out _)
                && Directory.GetCreationTimeUtc(folder) < DateTime.UtcNow.AddDays(-1))
            {
                Remove(new DirectoryInfo(folder));
            }
        }
    }

    #endregion
}
