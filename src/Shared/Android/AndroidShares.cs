namespace Drover.App.Platforms.Android;

/// <summary>
/// Takes what another app shares to Drover: words, files, or both. The files are copied into the app's cache at
/// once, while Android still lets this app read them, and nothing leaves the phone until the person has chosen a
/// conversation and sent the draft.
/// </summary>
public sealed class AndroidShares(ShareInbox inbox, IncomingNavigation navigation, ILogger<AndroidShares> logger)
{
    #region Private Fields

    // As many as the composer takes at once. Each one past a message's limit says so there.
    private const int MaxFiles = 100;

    private const string Folder = "shared-in";

    #endregion

    #region Public Methods

    public static bool IsShare(Intent? intent) => intent?.Action is Intent.ActionSend or Intent.ActionSendMultiple;

    public async Task Receive(Intent intent)
    {
        try
        {
            var text = intent.GetStringExtra(Intent.ExtraText)?.Trim() ?? string.Empty;
            var files = await Task.Run(() => Stage(intent));

            if (text.Length == 0 && files.Count == 0)
            {
                return;
            }

            inbox.Offer(new SharedDraft(text, files));
            navigation.Open(Addresses.Share);
        }
        catch (Exception exception) when (exception is Java.Lang.Exception or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Drover could not take what another app shared");
        }
    }

    #endregion

    #region Private Methods

    // Android puts the shared streams on the intent's clip. Only content addresses are read: a file address could
    // name one of this app's own files, and another app must not be able to have those attached.
    private List<IPickedFile> Stage(Intent intent)
    {
        var root = System.IO.Path.Combine(FileSystem.CacheDirectory, Folder);

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        List<IPickedFile> files = [];
        var resolver = AndroidApplication.Context.ContentResolver!;

        for (var index = 0; index < (intent.ClipData?.ItemCount ?? 0) && files.Count < MaxFiles; index++)
        {
            if (intent.ClipData!.GetItemAt(index)?.Uri is { Scheme: ContentResolver.SchemeContent } address)
            {
                files.Add(Copy(resolver, address, Directory.CreateDirectory(System.IO.Path.Combine(root, Guid.NewGuid().ToString("N")))));
            }
        }

        return files;
    }

    private static StagedFile Copy(ContentResolver resolver, AndroidUri address, DirectoryInfo folder)
    {
        var path = System.IO.Path.Combine(folder.FullName, NameOf(resolver, address));

        using (var source = resolver.OpenInputStream(address) ?? throw new IOException("Android did not open a shared file."))
        using (var target = File.Create(path))
        {
            source.CopyTo(target);
        }

        return new StagedFile(new FileInfo(path), resolver.GetType(address) ?? "application/octet-stream");
    }

    private static string NameOf(ContentResolver resolver, AndroidUri address)
    {
        using var cursor = resolver.Query(address, [global::Android.Provider.IOpenableColumns.DisplayName], null, null, null);
        var name = cursor is not null && cursor.MoveToFirst() && !cursor.IsNull(0) ? cursor.GetString(0) : null;

        // The name comes from the other app. Only its last part is used, inside a folder of this file's own.
        return System.IO.Path.GetFileName((name ?? string.Empty).Replace('\\', '/')) is { Length: > 0 } leaf && leaf is not ("." or "..")
            ? leaf
            : "Shared file";
    }

    #endregion

    #region Nested Types

    private sealed class StagedFile(FileInfo file, string contentType) : IBrowserFile, IPickedFile
    {
        #region Interface Implementations

        public string Name => file.Name;

        public DateTimeOffset LastModified => file.LastWriteTimeUtc;

        public long Size => file.Length;

        public string ContentType => contentType;

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => file.OpenRead();

        public Task<Stream> OpenRead(long maxBytes, CancellationToken cancellationToken) => Task.FromResult<Stream>(file.OpenRead());

        #endregion
    }

    #endregion
}
