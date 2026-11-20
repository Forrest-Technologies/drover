namespace Drover.Native.Platform;

/// <summary>The device's side of the shell: no address bar to keep in step, and a server to choose first.</summary>
internal static class PlatformShell
{
    #region Private Fields

    private static Action? openIncoming;

    #endregion

    #region Properties

    /// <summary>
    /// Where the system asked the app to go from outside it: a tapped notification, or a browser back from a tool
    /// server's sign-in. It outlives a restart of the interface, because the system can ask before the app has started.
    /// </summary>
    public static IncomingNavigation Incoming { get; } = new();

    #endregion

    #region Public Methods

    /// <summary>The screen that comes before every other while no server is chosen, or null once one is.</summary>
    public static Type? ScreenBeforeServer(IServiceProvider services) =>
        services.GetRequiredService<ServerAddress>().Current is null ? typeof(ConnectPage) : null;

    /// <summary>Reads what the device says about pointers and motion, before any control is made.</summary>
    public static void ReadDevice() => Motion.IsReduced = !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    /// <summary>Opens an address the system asked for, now and whenever another comes, once a server is chosen.</summary>
    public static void Start(Navigator navigator)
    {
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Incoming.Changed -= openIncoming;
        openIncoming = () => queue.TryEnqueue(() => OpenIncoming(navigator));
        Incoming.Changed += openIncoming;
        OpenIncoming(navigator);
#if ANDROID
        BackButton.Follow(navigator);
#endif
    }

    public static void GoBack(Navigator navigator) => navigator.GoBack();

    public static async Task OpenOutside(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new DeviceCallFailedException("The sign-in address must use HTTP or HTTPS.");
        }

        try
        {
            if (!await Launcher.LaunchUriAsync(uri))
            {
                throw new DeviceCallFailedException("The sign-in page could not be opened. Try again.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or UnauthorizedAccessException or IOException or System.Runtime.InteropServices.ExternalException)
        {
            throw new DeviceCallFailedException("The sign-in page could not be opened.", exception);
        }
    }

    public static void SetTitle(Window window, string title) => window.Title = title;

    // Before a server is chosen the address waits: the connect screen comes first.
    private static void OpenIncoming(Navigator navigator)
    {
        var services = ((App)Application.Current).Services;
        if (services.GetRequiredService<ServerAddress>().Current is not null && Incoming.TakeOpening() is { } opening)
        {
            // The conversation may be in a space other than the person's own, so work in its space first.
            if (opening.Space is { } space)
            {
                services.GetRequiredService<SessionState>().ChooseSpace(space);
            }

            navigator.NavigateTo(opening.Path);
        }
    }

    public static void Shown()
    {
    }

    /// <summary>The address of a screen in the web app of the chosen server.</summary>
    public static string? WebAddress(IServiceProvider services, string address) =>
        services.GetRequiredService<ServerAddress>().Current is { } origin ? new Uri(origin, address).AbsoluteUri : null;

    #endregion
}
