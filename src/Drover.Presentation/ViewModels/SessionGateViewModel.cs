namespace Drover.Presentation.ViewModels;

/// <summary>
/// Lets a signed-in person through to the screen behind it. Anyone else is sent to sign in, or to setup on a new
/// server, and a server that does not answer is said in words with a way to try again. Once a person is through,
/// notifications they had on at the client's former address are taken up again (<see cref="MovedNotifications"/>).
/// </summary>
public sealed partial class SessionGateViewModel(
    SessionState session,
    Navigator navigator,
    ILogger<SessionGateViewModel> logger,
    MovedNotifications? movedNotifications = null) : ObservableObject
{
    #region Private Fields

    [ObservableProperty]
    private GateState state = session.Current is null ? GateState.Checking : GateState.SignedIn;

    #endregion

    #region Properties

    public static string UnreachableTitle => "The server did not answer";

    public static string UnreachableCause => Failures.Unreachable;

    #endregion

    #region Public Methods

    [RelayCommand]
    public async Task Check()
    {
        State = session.Current is null ? GateState.Checking : GateState.SignedIn;
        try
        {
            if (await session.Load() is null)
            {
                navigator.NavigateTo(await session.FindEntryAddress(), replace: true);
            }
            else
            {
                State = session.Unreachable ? GateState.Unreachable : GateState.SignedIn;

                if (movedNotifications is not null)
                {
                    await movedNotifications.Resume();
                }
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(SessionGateViewModel));
            State = GateState.Unreachable;
        }
    }

    /// <summary>
    /// Asks the server again. A screen that could not reach it is opened again, so it loads what it shows.
    /// </summary>
    [RelayCommand]
    public async Task TryAgain()
    {
        if (session.Unreachable)
        {
            navigator.NavigateTo(navigator.Current, replace: true);
        }

        await Check();
    }

    /// <summary>Follows whether the screen behind the gate reached the server, while the gate is shown.</summary>
    public void Watch()
    {
        session.ReachChanged -= ShowReach;
        session.ReachChanged += ShowReach;
    }

    public void Unwatch() => session.ReachChanged -= ShowReach;

    #endregion

    #region Private Methods

    private void ShowReach()
    {
        if (session.Unreachable)
        {
            State = GateState.Unreachable;
        }
        else if (State is GateState.Unreachable)
        {
            if (session.Current is not null)
            {
                State = GateState.SignedIn;
            }
            else
            {
                _ = Check();
            }
        }
    }

    #endregion
}
