namespace Drover.Native.Shell;

/// <summary>
/// Starts the checks from a fresh install's state, with no server chosen, and lets the app reach the harness's
/// test server, whose certificate no platform trusts.
/// </summary>
public static partial class AppServices
{
    #region Private Fields

    private static bool forgotten;

    #endregion

    #region Private Methods

    static partial void AddChecks(IServiceCollection services)
    {
        if (Checks.CheckSettings.Current is not { } settings)
        {
            return;
        }

        // Once per run: the app starts its services again when a person chooses another server.
        if (!forgotten && !settings.ChecksRestart)
        {
            new ServerAddress().Forget();
            forgotten = true;
        }

        services.AddSingleton(provider => new AppRequests(
            provider.GetRequiredService<ServerAddress>(),
            provider.GetRequiredService<NativeSession>(),
            settings.Transport()));
    }

    #endregion
}
