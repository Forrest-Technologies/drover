namespace Drover.Native.Platform;

/// <summary>
/// Lets the person point the app at another server. The saved sign-in is kept per server, so going back to a
/// server finds it again.
/// </summary>
internal sealed class DeviceServerChoice(ServerAddress server) : IServerChoice
{
    #region Interface Implementations

    public string Host => server.Current?.Host ?? string.Empty;

    public Task Change()
    {
        ShareInbox.Process.Discard();
        server.Forget();

        // The app starts again with nothing kept from the server that was left.
        ((App)Application.Current).Restart();
        return Task.CompletedTask;
    }

    #endregion
}
