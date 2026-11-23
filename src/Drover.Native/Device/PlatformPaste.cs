namespace Drover.Native.Platform;

/// <summary>
/// On a device the platform's clipboard carries files and pictures itself, so the composer reads it when the field is
/// pasted into. Nothing arrives from outside, as it does from a browser's paste event.
/// </summary>
internal static class PlatformPaste
{
    #region Public Methods

    /// <summary>A composer on screen. A device's clipboard is read by the composer, so nothing is kept.</summary>
    public static void Join(Composer composer)
    {
    }

    /// <summary>A composer that left the screen.</summary>
    public static void Leave(Composer composer)
    {
    }

    #endregion
}
