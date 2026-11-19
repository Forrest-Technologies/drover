namespace Drover.Native.Droid;

/// <summary>
/// Android's Back, by button or gesture, takes one step back through the navigator: it closes an open dialog or rail
/// first, then goes to the screen before. The app listens only while such a step exists, so from the first screen
/// with nothing open Android leaves the app as it does for any other.
/// </summary>
internal static class BackButton
{
    #region Private Fields

    private static Navigator? followed;

    #endregion

    #region Public Methods

    /// <summary>Follows the navigator of the interface now running, in place of any before it.</summary>
    public static void Follow(Navigator navigator)
    {
        followed?.StepBackChanged -= Listen;
        followed = navigator;
        navigator.StepBackChanged += Listen;
        Listen();
    }

    #endregion

    #region Private Methods

    // From Android 16 the system asks the app only while it listens, and ignores whether it handled the request.
    private static void Listen()
    {
        var system = Windows.UI.Core.SystemNavigationManager.GetForCurrentView();
        system.BackRequested -= StepBack;
        if (followed?.CanStepBack == true)
        {
            system.BackRequested += StepBack;
        }
    }

    private static void StepBack(object? sender, Windows.UI.Core.BackRequestedEventArgs args) =>
        args.Handled = followed?.StepBack() is BackStep.Closed or BackStep.WentBack or BackStep.Held;

    #endregion
}
