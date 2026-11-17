namespace Drover.Native.Platform;

/// <summary>The device's side of the shell: no address bar to keep in step, and a server to choose first.</summary>
internal static class PlatformShell
{
    #region Public Methods

    /// <summary>The screen that comes before every other while no server is chosen, or null once one is.</summary>
    public static Type? ScreenBeforeServer(IServiceProvider services) =>
        services.GetRequiredService<ServerAddress>().Current is null ? typeof(ConnectPage) : null;

    /// <summary>Reads what the device says about pointers and motion, before any control is made.</summary>
    public static void ReadDevice() => Motion.IsReduced = !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    public static void Start(Navigator navigator)
    {
    }

    public static void GoBack(Navigator navigator) => navigator.GoBack();

    public static void SetTitle(Window window, string title) => window.Title = title;

    /// <summary>The address of a screen in the web app of the chosen server.</summary>
    public static string? WebAddress(IServiceProvider services, string address) =>
        services.GetRequiredService<ServerAddress>().Current is { } origin ? new Uri(origin, address).AbsoluteUri : null;

    #endregion
}
