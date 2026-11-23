namespace Drover.Native.Shell;

/// <summary>
/// The services of the app, in one place. Whatever a view model asks for in its constructor comes from here. A
/// view model itself is not registered: a screen makes its own with <see cref="Screen.Make{T}"/>, which fills the
/// constructor from these. What differs between a browser and a device is added by <c>AddPlatform</c>, which each
/// platform folder defines once.
/// </summary>
public static partial class AppServices
{
    #region Public Methods

    public static ServiceProvider Build()
    {
        var services = new ServiceCollection()
            .AddLogging(logging => logging
                .SetMinimumLevel(LogLevel.Warning)
                .AddPlatformLog()
                .AddDiagnosticLog(AppDiagnostics.Log))
            .AddSingleton(TimeProvider.System)
            .AddSingleton(AppDiagnostics.Log)
            .AddSingleton(AppDiagnostics.ThisApp)
            .AddSingleton<Navigator>()
            .AddSingleton<SessionState>()
            .AddSingleton<ILiveNotifications, NoLiveNotifications>()
            .AddSingleton<ToastService>()
            .AddSingleton<ExtensionHandoff>()
            .AddSingleton<ThemeModel>()
            .AddSingleton(ShareInbox.Process)
            .AddSingleton<MovedNotifications>()
            .AddPlatform();
        AddChecks(services);
        return services.BuildServiceProvider();
    }

    #endregion

    #region Private Methods

    // A build made for the screen checks (tests/Drover.Native.UITests) trusts its test server here. Any other build
    // has no body for this, so the call is not compiled.
    static partial void AddChecks(IServiceCollection services);

    #endregion
}
