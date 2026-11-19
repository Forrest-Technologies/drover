namespace Drover.Native.Shell;

/// <summary>
/// Starts the checks from a fresh install's state, with no server chosen, and lets the app reach the harness's
/// test server, whose certificate no platform trusts.
/// </summary>
public static partial class AppServices
{
#if !DROVER_BROWSER
    #region Private Fields

    private static bool forgotten;

    #endregion
#endif

    #region Private Methods

    static partial void AddChecks(IServiceCollection services)
    {
#if !DROVER_BROWSER
        if (Checks.CheckSettings.Current is not { } settings)
        {
            return;
        }

        // Once per run: the app starts its services again when a person chooses another server.
        if (!forgotten)
        {
            new ServerAddress().Forget();
            forgotten = true;
        }

        services.AddSingleton(provider => new AppRequests(
            provider.GetRequiredService<ServerAddress>(),
            provider.GetRequiredService<NativeSession>(),
            settings.Transport()));
#else
        // A browser is already on the server that served the page. There is no saved address to forget.
        _ = services;
#endif
    }

    #endregion
}
