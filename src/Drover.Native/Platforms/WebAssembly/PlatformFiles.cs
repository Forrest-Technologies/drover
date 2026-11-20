namespace Drover.Native.Platform;

/// <summary>
/// The browser saves a download link: an extension, or the diagnostic log. No runtime script evaluation or
/// file-system permission is needed.
/// </summary>
internal static partial class PlatformFiles
{
    #region Public Methods

    public static Task<bool> SaveText(string name, string text) => Save(name, text, "application/json;charset=utf-8");

    public static Task<bool> SaveLog(string name, string text) => Save(name, text, "text/plain;charset=utf-8");

    /// <summary>Saves text of the given media type under the name, as a download the browser keeps.</summary>
    public static Task<bool> SaveText(string name, string text, string mediaType) => Save(name, text, $"{mediaType};charset=utf-8");

    #endregion

    #region Private Methods

    private static async Task<bool> Save(string name, string text, string type)
    {
        try
        {
            await Download(name, text, type);
            return true;
        }
        catch (JSException exception)
        {
            throw new DeviceCallFailedException("The browser refused the download.", exception);
        }
    }

    [JSImport("globalThis.droverFiles.download")]
    private static partial Task Download(string name, string text, string type);

    #endregion
}
