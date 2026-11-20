namespace Drover.Presentation.ViewModels;

/// <summary>The CreateAccount setup step, with the same server calls and transitions as the existing client.</summary>
public sealed partial class CreateAccountViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigator,
    ILogger<CreateAccountViewModel> logger) : ObservableObject
{
    #region Private Fields

    [ObservableProperty]
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
    private string? problem;

    [ObservableProperty]
    private bool busy;

    #endregion

    #region Public Methods

    public Task Open() => CheckServer();

    // Setup happens once. After that this address has nothing to offer, so it hands over to sign-in.
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
        catch (DroverApiException exception) when (exception.StatusCode is System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(CreateAccountViewModel));
            Problem = "This server already has its first account. Sign in with it.";
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(CreateAccountViewModel));
            Problem = Failures.Sentence(exception, "Check that the server is running.");
        }
        finally
        {
            Busy = false;
        }
    }

    #endregion
}
