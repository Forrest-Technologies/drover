namespace Drover.Presentation.ViewModels;

/// <summary>The ConnectModel setup step, with the same server calls and transitions as the existing client.</summary>
public sealed partial class ConnectModelViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigator,
    ILogger<ConnectModelViewModel> logger) : ObservableObject, IDisposable
{
    #region Private Fields

    public const string CompatibleKind = NamedProviders.CompatibleKind;

    private NamedProviders? named;

    private CancellationTokenSource? poll;

    [ObservableProperty]
    private string baseUrl = string.Empty;

    [ObservableProperty]
    private string apiKey = string.Empty;

    [ObservableProperty]
    private ModelConnectionSummary? connection;

    [ObservableProperty]
    private string? sentKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Passed), nameof(Outcome))]
    private ConnectionTestResponse? result;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Passed), nameof(Outcome))]
    private string? problem;

    [ObservableProperty]
    private bool busy;

    [ObservableProperty]
    private string? userCode;

    [ObservableProperty]
    private string? verificationAddress;

    [ObservableProperty]
    private bool waitingForApproval;

    #endregion

    #region Properties

    /// <summary>The provider choice: Venice, the server's named providers, another compatible server.</summary>
    public NamedProviders Named => named ??= MakeNamed();

    public string Kind => Named.Kind;

    public bool Passed => Problem is null && Result is { Succeeded: true };

    public string? Outcome => Problem ?? Result?.Message;

    public bool IsSignIn => Named.IsSignIn;

    public string SignInName => Named.SignInName;

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Public Methods

    public async Task Open()
    {
        await session.Load();
        await Named.Load();
    }

    public void ChooseProvider(string chosen)
    {
        StopWaiting();
        if (Named.Choose(chosen) is { } address)
        {
            BaseUrl = address;
        }

        Result = null;
        Problem = null;
        UserCode = null;
        VerificationAddress = null;
    }

    [RelayCommand]
    public void ChangeAddress() => Named.RevealAddress();

    [RelayCommand]
    public Task RetryProviders() => Named.Load();

    [RelayCommand]
    public Task Test() => IsSignIn ? StartSignIn() : TestKey();

    [RelayCommand]
    public void Continue() => navigator.NavigateTo(Addresses.SetupBot);

    public void Dispose() => StopWaiting();

    #endregion

    #region Private Methods

    private async Task TestKey()
    {
        Busy = true;
        Problem = null;
        Result = null;
        try
        {
            Connection = await SaveConnection();
            Result = await api.TestModelConnection(SpaceId, Connection.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ConnectModelViewModel));
            Problem = Failures.Sentence(exception, "Check that the server is running.");
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task StartSignIn()
    {
        StopWaiting();
        Busy = true;
        Problem = null;
        Result = null;
        UserCode = null;
        VerificationAddress = null;
        try
        {
            var started = await api.StartModelSignIn(SpaceId, new(Kind, null));
            UserCode = started.UserCode;
            VerificationAddress = started.Address;
            WaitingForApproval = true;
            Busy = false;
            await WaitForApproval(started.AttemptId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ConnectModelViewModel));
            Problem = Failures.Sentence(exception, "Check that the server is running.");
            WaitingForApproval = false;
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task WaitForApproval(Guid attemptId)
    {
        poll = new CancellationTokenSource();
        var token = poll.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                var progress = await api.ReadModelSignIn(SpaceId, attemptId, token);

                if (progress.Status == ModelSignInStatuses.Done && progress.Connection is { } made)
                {
                    Connection = made;
                    Result = new ConnectionTestResponse(true, "Signed in. Usage bills the signed-in plan.", null, made);
                    WaitingForApproval = false;
                    return;
                }

                if (progress.Status is ModelSignInStatuses.Refused or ModelSignInStatuses.Expired)
                {
                    Problem = progress.Status == ModelSignInStatuses.Expired
                        ? "Sign-in took too long. Start it again."
                        : "Sign-in was cancelled.";
                    WaitingForApproval = false;
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogTrace("The sign-in wait on the {Screen} screen ended", nameof(ConnectModelViewModel));
            WaitingForApproval = false;
        }
    }

    private void StopWaiting()
    {
        poll?.Cancel();
        poll?.Dispose();
        poll = null;
        WaitingForApproval = false;
    }

    // The first test saves the Connection. A later one keeps it and only replaces a changed key,
    // unless the provider or the address changed, which makes it a different Connection: then the
    // space is asked first, because a visit to this step may have made one for it already, and a
    // second row for the same provider and address would sit on the Models screen as another one.
    private async Task<ModelConnectionSummary> SaveConnection()
    {
        var address = Kind == CompatibleKind ? BaseUrl.Trim() : null;
        var key = string.IsNullOrWhiteSpace(ApiKey) || !Named.ShowsKey ? null : ApiKey.Trim();

        if (Connection is null || Connection.Kind != Kind || !string.Equals(Connection.BaseUrl, address, StringComparison.Ordinal))
        {
            var reused = (await api.ListModelConnections(SpaceId))
                .FirstOrDefault(saved => saved.Kind == Kind && string.Equals(saved.BaseUrl, address, StringComparison.Ordinal));

            if (reused is not null)
            {
                Connection = reused;
                SentKey = null;
            }
            else
            {
                var made = await api.CreateModelConnection(SpaceId, new(Kind, Named.NameFor(address), address, key, null));
                SentKey = key;
                return made;
            }
        }

        if (key is not null && key != SentKey)
        {
            var updated = await api.ReplaceModelKey(SpaceId, Connection.Id, new(key));
            SentKey = key;
            return updated;
        }

        return Connection;
    }

    private NamedProviders MakeNamed()
    {
        var made = new NamedProviders(api, logger);
        made.Changed += () => OnPropertyChanged(string.Empty);
        return made;
    }

    #endregion
}
