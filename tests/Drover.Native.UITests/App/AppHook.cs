namespace Drover.Native;

/// <summary>Starts the screen checks once the window is up, when the harness asked for them.</summary>
public partial class App
{
    #region Private Methods

    partial void StartChecks(Window window)
    {
        if (Checks.CheckSettings.Current is { } settings)
        {
            _ = new Checks.ScreenChecks(window, settings).Run();
        }
    }

    #endregion
}
