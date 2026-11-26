namespace Drover.Native.Platform;

/// <summary>
/// What the app is given in a browser. It is served by its server, so it talks to its own origin: the browser
/// holds the session cookie, which script cannot read, and every request that changes something carries the
/// antiforgery token. No token is kept in storage. There is no server to choose. Notifications go through the
/// service worker the page registers. A tool server's sign-in comes back to the path the app is served under.
/// </summary>
internal static class PlatformServices
{
    #region Public Methods

    public static IServiceCollection AddPlatform(this IServiceCollection services) =>
        services
            .AddSingleton<IDeviceSettings, BrowserSettings>()
            .AddSingleton(new UiEnvironment(IsDevelopment: BuildKind.IsDevelopment))
            .AddSingleton<IPushRegistration, BrowserPushRegistration>()
            .AddSingleton(new AppSignInReturn(PlatformShell.ClientPath))
            .AddSingleton(new CallConnection(null))
            .AddSingleton<BrowserAudio>()
            .AddSingleton<IAudioCapture>(provider => provider.GetRequiredService<BrowserAudio>())
            .AddSingleton<IAudioPlayback>(provider => provider.GetRequiredService<BrowserAudio>())
            .AddSingleton(_ => new DroverApi(new HttpClient(BrowserRequests()) { BaseAddress = new($"{Browser.Origin}/") }));

    /// <summary>Log lines go to the browser's console.</summary>
    public static ILoggingBuilder AddPlatformLog(this ILoggingBuilder logging) =>
        logging.AddProvider(new Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());

    #endregion

    #region Private Methods

    private static AntiforgeryHandler BrowserRequests() => new(PageCookies.Read) { InnerHandler = new HttpClientHandler() };

    #endregion
}
