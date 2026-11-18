namespace Drover.Presentation.ViewModels;

/// <summary>
/// First-run setup, step one: the account that owns the server. Setup happens once; after that this address has
/// nothing to offer, so it hands over to sign-in.
/// </summary>
public sealed partial class CreateAccountViewModel(
    DroverApi api, SessionState session, Navigator navigator, ILogger<CreateAccountViewModel> logger) : ObservableObject
{
    #region Private Fields

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChecking), nameof(ShowFailed), nameof(ShowForm), nameof(RequiresCode))]
    private SetupStatus? status;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string setupCode = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChecking), nameof(ShowFailed))]
    private string? problem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubmitLabel))]
    private bool busy;

    #endregion

    #region Properties

    public static string Title => "Set up Drover";

    public static string Heading => "Create your account";

    public static string CheckingNote => "Checking this server.";

    public static string Lead => "This is the first account on this server. It owns the server and everything on it.";

    public static string PasswordHint => "At least 12 characters.";

    public static string SetupCodeHint => "Drover printed this code in its server log when it started. It proves you run this server.";

    public bool ShowChecking => Status is null && Problem is null;

    public bool ShowFailed => Status is null && Problem is not null;

    public bool ShowForm => Status is not null;

    public bool RequiresCode => Status is { RequiresCode: true };

    public string SubmitLabel => Busy ? "Creating your account" : "Create account";

    #endregion

    #region Public Methods

    [RelayCommand]
    public async Task CheckServer()
    {
        Problem = null;
        try
        {
            Status = await api.GetSetupStatus();
            if (Status is null)
            {
                navigator.NavigateTo(Addresses.SignIn, replace: true);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(CreateAccountViewModel));
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
            var code = Status is { RequiresCode: true } ? SetupCode.Trim() : null;
            session.Begin(await api.CreateFirstAccount(new(Email.Trim(), DisplayName.Trim(), Password, code)));
            navigator.NavigateTo(Addresses.SetupModel);
        }
        catch (DroverApiException exception) when (exception.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(CreateAccountViewModel));
            Problem = "This server already has its first account. Sign in with it.";
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(CreateAccountViewModel));
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Busy = false;
        }
    }

    #endregion
}
