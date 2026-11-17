namespace Drover.App;

/// <summary>
/// Lets the person point the app at another server. The saved sign-in and notification registration are kept
/// per server, so going back to a server finds them again.
/// </summary>
public sealed class AppServerChoice(ServerAddress server, NavigationManager navigation) : IServerChoice
{
    #region Interface Implementations

    public string Host => server.Current?.Host ?? string.Empty;

    public Task Change()
    {
        server.Forget();

        // A full load starts the interface again with nothing kept from the server that was left.
        navigation.NavigateTo("/", forceLoad: true);
        return Task.CompletedTask;
    }

    #endregion
}
