namespace Drover.Native.Platform;

/// <summary>
/// What the app is given on a device. It talks to the one server its person chose, over HTTPS only, following no
/// redirects, with cookies kept in memory. Only the server's address is kept in plain settings; the sign-in that
/// outlives the app goes to the platform's credential store. What only one kind of device has (notifications and
/// shares on a phone, saving files on a desktop) is added by <c>AddDevice</c>, which each platform folder defines.
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
            .AddSingleton<ISecureStorage, DeviceSecrets>()
            .AddSingleton<NativeSession>()
            .AddSingleton<AppRequests>()
            .AddSingleton<IServerChoice, DeviceServerChoice>()
            .AddSingleton(PlatformShell.Incoming)
            .AddDevice()
            .AddSingleton(provider => new DroverApi(new HttpClient(provider.GetRequiredService<AppRequests>(), disposeHandler: false)
            {
                BaseAddress = AppRequests.Placeholder,
            }));

    #endregion
}
