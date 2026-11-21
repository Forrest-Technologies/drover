namespace Drover.Native.Platform;

/// <summary>
/// The browser's side of the shell. The address bar and the browser's history are kept in step with the app's
/// navigator in both directions: going somewhere in the app adds a history entry and changes the address, and the
/// browser's back and forward buttons move the app. Every address is therefore a link that can be opened, shared,
/// and reloaded. When the screen holds work, the browser's buttons ask first like any other way of leaving, and so
/// does closing or reloading the tab.
/// </summary>
internal static class PlatformShell
{
    #region Private Fields

    private const string BasePathVariable = "UNO_BOOTSTRAP_WEBAPP_BASE_PATH";

    // The folder the app is served from, without a trailing slash: "/native", or nothing when it is at the root.
    private static readonly string basePath = (Environment.GetEnvironmentVariable(BasePathVariable) ?? "/").TrimEnd('/');

    // The address the browser shows, so a step the app takes itself through history can be matched in the browser.
    private static string shown = string.Empty;

    // True while the app follows the browser, which has already moved.
    private static bool arriving;

    // True after the shell moved the browser's history itself, until the browser says it arrived.
    private static bool stepping;

    #endregion

    #region Properties

    /// <summary>The path the app is served under, with its trailing slash: "/native/", or "/" at the root.</summary>
    public static string ClientPath => $"{basePath}/";

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
        shown = navigator.Current;
        navigator.Navigated += (address, kind) => Follow(navigator, address, kind);
        Browser.Listen("popstate", () => Arrive(navigator));
        Browser.Listen("beforeunload", leaving => AskBeforeClosing(navigator, leaving));
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

    private static void Follow(Navigator navigator, string address, NavigationKind kind)
    {
        switch (kind)
        {
            case NavigationKind.Push:
                Browser.PushState(null, string.Empty, $"{basePath}{address}");
                break;
            case NavigationKind.Replace:
                Browser.ReplaceState(null, string.Empty, $"{basePath}{address}");
                break;
            case NavigationKind.Travel when !arriving:
                StepBrowser(navigator);
                break;
        }

        shown = address;
    }

    // The app took a step through its history itself, so the browser takes the same step. Its popstate then arrives
    // where the app already is, and is let pass.
    private static void StepBrowser(Navigator navigator)
    {
        stepping = true;
        if (navigator.Next == shown)
        {
            Browser.GoBack();
        }
        else
        {
            Browser.GoForward();
        }
    }

    private static void Arrive(Navigator navigator)
    {
        if (stepping)
        {
            stepping = false;
            return;
        }

        var address = InApp(Browser.Address);
        arriving = true;
        try
        {
            if (navigator.Arrive(address))
            {
                shown = navigator.Current;
                return;
            }
        }
        finally
        {
            arriving = false;
        }

        PutAddressBack(navigator, address);
    }

    // The app stayed, so the browser goes back to the entry the app is at. Its popstate is let pass.
    private static void PutAddressBack(Navigator navigator, string address)
    {
        if (address == navigator.Previous)
        {
            stepping = true;
            Browser.GoForward();
        }
        else if (address == navigator.Next)
        {
            stepping = true;
            Browser.GoBack();
        }
        else
        {
            Browser.PushState(null, string.Empty, $"{basePath}{navigator.Current}");
        }
    }

    // A non-empty return value is how a page asks the browser to confirm before it closes or reloads.
    private static void AskBeforeClosing(Navigator navigator, JSObject leaving)
    {
        using (leaving)
        {
            if (navigator.HoldsWork?.Invoke() is true)
            {
                leaving.SetProperty("returnValue", "unsaved");
            }
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
