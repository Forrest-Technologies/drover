namespace Drover.Native.Platform;

/// <summary>
/// The few things the app asks of the browser itself: the page's local storage, its address and history, a media
/// query, a cookie. Each is a direct call to a browser function, with no script of the app's own in between.
/// </summary>
internal static partial class Browser
{
    #region Properties

    public static string Origin => Location.GetPropertyAsString("origin") ?? string.Empty;

    /// <summary>The path, query, and fragment in the address bar.</summary>
    public static string Address =>
        $"{Location.GetPropertyAsString("pathname")}{Location.GetPropertyAsString("search")}{Location.GetPropertyAsString("hash")}";

    public static string Cookies => Document.GetPropertyAsString("cookie") ?? string.Empty;

    private static JSObject Location => JSHost.GlobalThis.GetPropertyAsJSObject("location")!;

    private static JSObject Document => JSHost.GlobalThis.GetPropertyAsJSObject("document")!;

    #endregion

    #region Public Methods

    [JSImport("globalThis.localStorage.getItem")]
    public static partial string? ReadStored(string name);

    [JSImport("globalThis.localStorage.setItem")]
    public static partial void Store(string name, string value);

    [JSImport("globalThis.localStorage.removeItem")]
    public static partial void Forget(string name);

    [JSImport("globalThis.history.pushState")]
    public static partial void PushState(JSObject? state, string unused, string address);

    [JSImport("globalThis.history.replaceState")]
    public static partial void ReplaceState(JSObject? state, string unused, string address);

    [JSImport("globalThis.history.back")]
    public static partial void GoBack();

    [JSImport("globalThis.addEventListener")]
    public static partial void Listen(string eventName, [JSMarshalAs<JSType.Function>] Action handler);

    public static bool Matches(string mediaQuery)
    {
        using var result = MatchMedia(mediaQuery);
        return result.GetPropertyAsBoolean("matches");
    }

    public static void SetTitle(string title) => Document.SetProperty("title", title);

    #endregion

    #region Private Methods

    [JSImport("globalThis.matchMedia")]
    private static partial JSObject MatchMedia(string mediaQuery);

    #endregion
}
