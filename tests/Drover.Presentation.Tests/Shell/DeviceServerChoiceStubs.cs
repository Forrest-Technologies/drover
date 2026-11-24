namespace Drover.Native.Platform;

/// <summary>The server address the real server-choice code forgets when the person points at another one.</summary>
internal sealed class ServerAddress
{
    #region Properties

    public Uri? Current => null;

    #endregion

    #region Public Methods

    public void Forget()
    {
    }

    #endregion
}

/// <summary>The app the real server-choice code restarts, stood in for so it can run without a device head.</summary>
internal static class Application
{
    #region Properties

    public static App Current { get; } = new();

    #endregion
}

/// <summary>Records that a restart was asked for, without starting a second interface in a test.</summary>
internal sealed class App
{
    #region Properties

    public bool Restarted { get; private set; }

    #endregion

    #region Public Methods

    public void Restart() => Restarted = true;

    #endregion
}
