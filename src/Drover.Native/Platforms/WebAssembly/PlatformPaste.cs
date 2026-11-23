namespace Drover.Native.Platform;

/// <summary>
/// In a browser a picture or a file copied to the clipboard reaches the page with its paste event, and the platform's
/// own clipboard holds text only. A small script listens for the event, reads each file, and hands it here.
/// </summary>
internal static partial class PlatformPaste
{
    #region Private Fields

    private static bool listening;

    #endregion

    #region Public Methods

    /// <summary>Raised for each file a paste carried: the file read, or a stand-in for one too large to read.</summary>
    public static event Action<IPickedFile>? Received;

    /// <summary>Starts listening for pastes once, however many composers ask.</summary>
    public static void Start()
    {
        if (listening)
        {
            return;
        }

        listening = true;
        Listen(PastedPicture.Limit, Taken, TooLarge);
    }

    #endregion

    #region Private Methods

    private static void Taken(string name, int id) => Received?.Invoke(PickedFiles.FromPasted(name, Collect(id)));

    private static void TooLarge(string name, double size) => Received?.Invoke(PickedFiles.TooLargeToRead(name, (long)size));

    [JSImport("globalThis.droverPaste.listen")]
    private static partial void Listen(
        double largest,
        [JSMarshalAs<JSType.Function<JSType.String, JSType.Number>>] Action<string, int> taken,
        [JSMarshalAs<JSType.Function<JSType.String, JSType.Number>>] Action<string, double> tooLarge);

    // The script keeps what it read until it is asked for, so bytes cross the boundary as a return value.
    [JSImport("globalThis.droverPaste.collect")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial byte[] Collect(int id);

    #endregion
}
