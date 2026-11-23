namespace Drover.Native.Platform;

/// <summary>
/// On a device the platform's clipboard carries files and pictures itself, so the composer reads it when the field is
/// pasted into. Nothing arrives from outside, as it does from a browser's paste event.
/// </summary>
internal static class PlatformPaste
{
    #region Public Methods

    /// <summary>Raised once for each paste, with the files it carried. Never raised on a device.</summary>
    public static event Action<IReadOnlyList<IPickedFile>>? Received
    {
        add { }
        remove { }
    }

    public static void Start()
    {
    }

    #endregion
}
