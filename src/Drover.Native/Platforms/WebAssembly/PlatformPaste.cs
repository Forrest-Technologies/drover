namespace Drover.Native.Platform;

/// <summary>
/// In a browser a picture or a file copied to the clipboard reaches the page with its paste event, and the platform's
/// own clipboard holds text only. A small script listens for the event and asks here, before it reads anything, which
/// message field takes the paste: the one with the cursor, while its conversation takes files. The files then go to
/// that conversation as one batch, however long reading them takes and wherever the cursor has gone meanwhile, unless
/// it stopped taking files.
/// </summary>
internal static partial class PlatformPaste
{
    #region Private Fields

    // At most this many bytes of pasted files are held at once, across every paste still being read. Two pictures of
    // the most a pasted picture may be.
    private const int MostBytesHeld = 2 * 64 * 1024 * 1024;

    private static readonly List<Composer> composers = [];

    private static readonly Dictionary<int, ComposerViewModel> destinations = [];

    private static bool listening;

    #endregion

    #region Public Methods

    /// <summary>A composer on screen may take what is pasted in its field. The script starts with the first.</summary>
    public static void Join(Composer composer)
    {
        composers.Add(composer);

        if (!listening)
        {
            listening = true;
            Listen(PastedPicture.Limit, MostBytesHeld, ComposerViewModel.MaxFilesAtOnce, Claim, Pasted, Failed);
        }
    }

    /// <summary>
    /// A composer that left the screen takes no new paste. One it took already still reaches its conversation, unless
    /// that conversation stopped taking files.
    /// </summary>
    public static void Leave(Composer composer) => composers.Remove(composer);

    #endregion

    #region Private Methods

    // Asked while the paste event runs: the paste is bound to the conversation whose field has the cursor now.
    private static bool Claim(int paste)
    {
        if (composers.Select(composer => composer.PasteDestination).FirstOrDefault(found => found is not null)
            is not { } destination)
        {
            return false;
        }

        destinations[paste] = destination;
        return true;
    }

    // Every file's bytes are collected before the first wait, since the script lets go of the rest once this returns.
    private static async void Pasted(int paste, string batch)
    {
        if (!destinations.Remove(paste, out var destination) || !destination.TakesFiles)
        {
            return;
        }

        var handed = Described(batch);

        if (handed.Files.Count == 0)
        {
            destination.Notify(FileWords.PasteFailed, ToastKind.Error);
            return;
        }

        try
        {
            await destination.Hand(handed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.For<Composer>().LogError(exception, "What was pasted could not be attached");
            destination.Notify(FileWords.PasteFailed, ToastKind.Error);
        }
    }

    private static void Failed(int paste)
    {
        if (destinations.Remove(paste, out var destination))
        {
            Log.For<Composer>().LogError("A file the browser handed over with a paste could not be read");
            destination.Notify(FileWords.PasteFailed, ToastKind.Error);
        }
    }

    private static HandedFiles Described(string batch)
    {
        using var described = JsonDocument.Parse(batch);
        var now = TimeProvider.System.GetLocalNow();
        List<IPickedFile> files = [];

        foreach (var file in described.RootElement.GetProperty("files").EnumerateArray())
        {
            var name = FileWords.PastedFileName(
                file.GetProperty("name").GetString() ?? string.Empty,
                file.GetProperty("made").GetBoolean(),
                now);
            var id = file.GetProperty("id").GetInt32();
            files.Add(id is 0
                ? PickedFiles.TooLargeToRead(name, file.GetProperty("size").GetInt64())
                : PickedFiles.FromBytes(Collect(id), name));
        }

        return new(files, Beyond: described.RootElement.GetProperty("beyond").GetInt32());
    }

    [JSImport("globalThis.droverPaste.listen")]
    private static partial void Listen(
        int largest,
        int budget,
        int most,
        [JSMarshalAs<JSType.Function<JSType.Number, JSType.Boolean>>] Func<int, bool> claim,
        [JSMarshalAs<JSType.Function<JSType.Number, JSType.String>>] Action<int, string> pasted,
        [JSMarshalAs<JSType.Function<JSType.Number>>] Action<int> failed);

    // The script keeps what it read until it is asked for, so bytes cross the boundary as a return value.
    [JSImport("globalThis.droverPaste.collect")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial byte[] Collect(int id);

    #endregion
}
