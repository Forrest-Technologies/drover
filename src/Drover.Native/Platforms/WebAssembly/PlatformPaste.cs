namespace Drover.Native.Platform;

/// <summary>
/// In a browser a picture or a file copied to the clipboard reaches the page with its paste event, and the platform's
/// own clipboard holds text only. A small script listens for the event, reads the files, and hands them here as one
/// batch, so the conversation counts them as one paste.
/// </summary>
internal static partial class PlatformPaste
{
    #region Private Fields

    private static bool listening;

    #endregion

    #region Public Methods

    /// <summary>Raised once for each paste, with every file it carried: read, or a stand-in for one too large to read.</summary>
    public static event Action<IReadOnlyList<IPickedFile>>? Received;

    /// <summary>Starts listening for pastes once, however many composers ask.</summary>
    public static void Start()
    {
        if (listening)
        {
            return;
        }

        listening = true;
        Listen(PastedPicture.Limit, ComposerViewModel.MaxFilesAtOnce + 1, Pasted);
    }

    #endregion

    #region Private Methods

    private static void Pasted(string batch)
    {
        using var described = JsonDocument.Parse(batch);
        List<IPickedFile> files = [];

        foreach (var file in described.RootElement.EnumerateArray())
        {
            var name = file.GetProperty("name").GetString() ?? string.Empty;
            var id = file.GetProperty("id").GetInt32();
            files.Add(id is 0
                ? PickedFiles.TooLargeToRead(name, file.GetProperty("size").GetInt64())
                : PickedFiles.FromPasted(name, Collect(id)));
        }

        Received?.Invoke(files);
    }

    [JSImport("globalThis.droverPaste.listen")]
    private static partial void Listen(
        double largest,
        int most,
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> pasted);

    // The script keeps what it read until it is asked for, so bytes cross the boundary as a return value.
    [JSImport("globalThis.droverPaste.collect")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial byte[] Collect(int id);

    #endregion
}
