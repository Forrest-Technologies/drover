namespace Drover.Ui.Session;

/// <summary>
/// Registered by a native shell only: the server the person pointed the app at, and the way to point it at
/// another. The web app has no such choice, because it is served by its server.
/// </summary>
public interface IServerChoice
{
    /// <summary>The server's host name, as the person typed it.</summary>
    string Host { get; }

    /// <summary>Forgets the server and asks for its address again.</summary>
    Task Change();
}
