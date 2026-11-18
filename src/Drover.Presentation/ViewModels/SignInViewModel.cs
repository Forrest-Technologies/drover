namespace Drover.Presentation.ViewModels;

/// <summary>Signing in with the account made on this server.</summary>
public sealed partial class SignInViewModel(
    DroverApi api, SessionState session, Navigator navigator, ILogger<SignInViewModel> logger) : ObservableObject
{
    #region Private Fields

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string? problem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubmitLabel))]
    private bool busy;

    #endregion

    #region Properties

    public static string Title => "Sign in to Drover";

    public string SubmitLabel => Busy ? "Signing in" : "Sign in";

    #endregion

    #region Public Methods

    /// <summary>Someone already signed in has no use for this screen, and a new server needs setup first.</summary>
    public async Task Load()
    {
        try
        {
            if (await session.Load() is not null)
            {
                navigator.NavigateTo(Addresses.Home, replace: true);
            }
            else if (await session.FindEntryAddress() is Addresses.Setup)
            {
                navigator.NavigateTo(Addresses.Setup, replace: true);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(SignInViewModel));
            Problem = Failures.Sentence(exception);
        }
    }

    [RelayCommand]
    public async Task Submit()
    {
        Busy = true;
        Problem = null;
        try
        {
            session.Begin(await api.SignIn(new(Email.Trim(), Password)));
            navigator.NavigateTo(Addresses.Home);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(SignInViewModel));
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Busy = false;
        }
    }

    #endregion
}
