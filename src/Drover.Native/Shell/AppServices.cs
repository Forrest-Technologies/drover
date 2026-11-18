namespace Drover.Native.Shell;

/// <summary>
/// The services of the app, in one place. Whatever a view model asks for in its constructor comes from here. A
/// view model itself is not registered: a screen makes its own with <see cref="Screen.Make{T}"/>, which fills the
/// constructor from these. What differs between a browser and a device is added by <c>AddPlatform</c>, which each
/// platform folder defines once.
/// </summary>
public static class AppServices
{
    public static ServiceProvider Build() =>
        new ServiceCollection()
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning).AddPlatformLog())
            .AddSingleton(TimeProvider.System)
            .AddSingleton<Navigator>()
            .AddSingleton<SessionState>()
            .AddSingleton<ToastService>()
            .AddSingleton<ExtensionHandoff>()
            .AddSingleton<ThemeModel>()
            .AddPlatform()
            .BuildServiceProvider();
}
