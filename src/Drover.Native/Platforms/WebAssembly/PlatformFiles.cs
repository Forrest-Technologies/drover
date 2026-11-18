namespace Drover.Native.Platform;

/// <summary>The browser saves a download link. No runtime script evaluation or file-system permission is needed.</summary>
internal static partial class PlatformFiles
{
    #region Public Methods

    public static Task<bool> SaveText(string name, string text) => Save(name, text);

    #endregion

    #region Private Methods

    private static async Task<bool> Save(string name, string text)
    {
        try
        {
            await Download(name, text);
            return true;
        }
        catch (JSException exception)
        {
            throw new DeviceCallFailedException("The browser refused the download.", exception);
        }
    }

    [JSImport("globalThis.droverFiles.download")]
    private static partial Task Download(string name, string text);

    #endregion
}
