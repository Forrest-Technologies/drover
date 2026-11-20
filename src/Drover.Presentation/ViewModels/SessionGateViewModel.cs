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

    public static string CheckingNote => "Checking who is signed in.";

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
                State = GateState.SignedIn;

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

    #endregion
}
