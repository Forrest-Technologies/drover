namespace Drover.Ui.Session;

/// <summary>
/// Opens a screen from a navigation the platform does not wait for. A request that fails on the way stays with
/// the screen's gate, instead of escaping into the platform and closing the app.
/// </summary>
public static class ScreenOpen
{
    #region Public Methods

    public static Task At(SessionState session, Func<Task> opened, ILogger logger) =>
        session.OpenScreen(opened, logger);

    #endregion
}
