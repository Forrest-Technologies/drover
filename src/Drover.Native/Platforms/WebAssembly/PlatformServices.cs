namespace Drover.Native.Platform;

/// <summary>
/// What the app is given in a browser. It is served by its server, so it talks to its own origin: the browser
/// holds the session cookie, which script cannot read, and every request that changes something carries the
/// antiforgery token. No token is kept in storage. There is no server to choose.
/// </summary>
internal static class PlatformServices
{
    #region Private Fields

    private const char CookieSeparator = ';';

    #endregion

    #region Public Methods

    public static IServiceCollection AddPlatform(this IServiceCollection services) =>
        services
            .AddSingleton<IDeviceSettings, BrowserSettings>()
            .AddSingleton(new UiEnvironment(IsDevelopment: BuildKind.IsDevelopment))
            .AddSingleton(_ => new DroverApi(new HttpClient(BrowserRequests()) { BaseAddress = new($"{Browser.Origin}/") }));

    /// <summary>Log lines go to the browser's console.</summary>
    public static ILoggingBuilder AddPlatformLog(this ILoggingBuilder logging) =>
        logging.AddProvider(new Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());

    #endregion

    #region Private Methods

    private static AntiforgeryHandler BrowserRequests() => new(ReadCookie) { InnerHandler = new HttpClientHandler() };

    private static ValueTask<string?> ReadCookie(string name, CancellationToken cancellationToken)
    {
        var prefix = $"{name}=";
        var found = Browser.Cookies
            .Split(CookieSeparator, StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith(prefix, StringComparison.Ordinal));

        return ValueTask.FromResult(found is null ? null : Uri.UnescapeDataString(found[prefix.Length..]));
    }

    #endregion
}
