namespace Drover.Native.Platform;

/// <summary>
/// What the app is given on a device. It talks to the one server its person chose, over HTTPS only, following no
/// redirects, with cookies kept in memory. Only the server's address is kept in plain settings. What differs by kind
/// of device (where the sign-in that outlives the app is kept, notifications and shares on a phone, saving files on a
/// desktop) is added by <c>AddDevice</c>, which each platform folder defines. A device that says when the app leaves
/// the screen adds a <see cref="StreamPresence"/>, and the live streams close while it is away.
/// </summary>
internal static class PlatformServices
{
    #region Public Methods

    /// <summary>Log lines go to the process's standard output, where the platform's own log collects them.</summary>
    public static ILoggingBuilder AddPlatformLog(this ILoggingBuilder logging) => logging.AddConsole();

    public static IServiceCollection AddPlatform(this IServiceCollection services) =>
        services
            .AddSingleton<IDeviceSettings, DeviceSettings>()
            .AddSingleton(new UiEnvironment(IsDevelopment: BuildKind.IsDevelopment))
            .AddSingleton<ServerAddress>()
            .AddSingleton<NativeSession>()
            .AddSingleton<AppRequests>()
            .AddSingleton<IServerChoice, DeviceServerChoice>()
            .AddSingleton(PlatformShell.Incoming)
            .AddDevice()
            .AddSingleton(provider => new DroverApi(
                new HttpClient(provider.GetRequiredService<AppRequests>(), disposeHandler: false)
                {
                    BaseAddress = AppRequests.Placeholder,
                },
                provider.GetService<StreamPresence>()));

    #endregion
}
