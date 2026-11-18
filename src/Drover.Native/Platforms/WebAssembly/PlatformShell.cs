namespace Drover.Native.Platform;

/// <summary>
/// The browser's side of the shell. The address bar and the browser's history are kept in step with the app's
/// navigator in both directions: going somewhere in the app adds a history entry and changes the address, and the
/// browser's back and forward buttons move the app. Every address is therefore a link that can be opened, shared,
/// and reloaded.
/// </summary>
internal static class PlatformShell
{
    #region Private Fields

    private const string BasePathVariable = "UNO_BOOTSTRAP_WEBAPP_BASE_PATH";

    // The folder the app is served from, without a trailing slash: "/native", or nothing when it is at the root.
    private static readonly string basePath = (Environment.GetEnvironmentVariable(BasePathVariable) ?? "/").TrimEnd('/');

    #endregion

    #region Public Methods

    /// <summary>The screen that comes before every other while no server is chosen. In a browser there is never one.</summary>
    public static Type? ScreenBeforeServer(IServiceProvider services) => null;

    /// <summary>Reads what the device says about pointers and motion, before any control is made.</summary>
    public static void ReadDevice()
    {
        Targets.IsCoarse = Browser.Matches("(pointer: coarse)");
        Motion.IsReduced = Browser.Matches("(prefers-reduced-motion: reduce)");
    }

    public static void Start(Navigator navigator)
    {
        navigator.StartAt(InApp(Browser.Address));
        navigator.Navigated += Follow;
        Browser.Listen("popstate", () => navigator.Arrive(InApp(Browser.Address)));
    }

    /// <summary>One step back, through the browser's own history so the two never disagree.</summary>
    public static void GoBack(Navigator navigator) => Browser.GoBack();

    public static Task OpenOutside(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new DeviceCallFailedException("The sign-in address must use HTTP or HTTPS.");
        }

        try
        {
            Browser.OpenOutside(uri.AbsoluteUri);
            return Task.CompletedTask;
        }
        catch (JSException exception)
        {
            throw new DeviceCallFailedException("The sign-in page could not be opened.", exception);
        }
    }

    public static void SetTitle(Window window, string title) => Browser.SetTitle(title);

    /// <summary>Rebuilds the semantic tree once the shell is attached, after the platform's early activation.</summary>
    public static void Shown() => Browser.RefreshAccessibility();

    /// <summary>The address of a screen in the web app, which is served from the root of the same origin.</summary>
    public static string? WebAddress(IServiceProvider services, string address) => $"{Browser.Origin}{address}";

    #endregion

    #region Private Methods

    private static void Follow(string address, NavigationKind kind)
    {
        switch (kind)
        {
            case NavigationKind.Push:
                Browser.PushState(null, string.Empty, $"{basePath}{address}");
                break;
            case NavigationKind.Replace:
                Browser.ReplaceState(null, string.Empty, $"{basePath}{address}");
                break;
        }
    }

    private static string InApp(string browserAddress)
    {
        var address = basePath.Length > 0 && browserAddress.StartsWith(basePath, StringComparison.Ordinal)
            ? browserAddress[basePath.Length..]
            : browserAddress;

        return address.StartsWith('/') ? address : $"/{address}";
    }

    #endregion
}
