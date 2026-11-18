namespace Drover.Native.Pages;

/// <summary>
/// Takes the person to a tool server's own sign-in page, outside the app. In a browser the page is left for it, as
/// the web client does, and the provider sends the browser back when done; on a device the system opens it. The
/// browser's side is in <c>Platforms/WebAssembly</c>, the device's in <c>Device</c>.
/// </summary>
internal static partial class ProviderPage
{
    #region Public Methods

    public static void Open(string address)
    {
        if (!TryLeaveThePage(address))
        {
            _ = Launcher.LaunchUriAsync(new Uri(address));
        }
    }

    #endregion

    #region Private Methods

    private static partial bool TryLeaveThePage(string address);

    #endregion
}
