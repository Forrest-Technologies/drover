namespace Drover.Presentation.ViewModels;

/// <summary>The ConnectModel setup step, with the same server calls and transitions as the existing client.</summary>
public sealed partial class ConnectModelViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigator,
    ILogger<ConnectModelViewModel> logger) : ObservableObject
{
    #region Private Fields

    private NamedProviders? named;

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
    private string? addressProblem;

    [ObservableProperty]
    private string? keyProblem;

    [ObservableProperty]
    private bool busy;

    #endregion

    #region Properties

    /// <summary>The provider choice and what the form shows for it, drawn from the kinds the server offers.</summary>
    public NamedProviders Named => named ??= MakeNamed();

    public string Kind => Named.Kind;

    public bool Passed => Problem is null && Result is { Succeeded: true };

    public string? Outcome => Problem ?? Result?.Message;

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
        if (Named.Choose(chosen) is { } address)
        {
            BaseUrl = address;
        }

        ApiKey = string.Empty;
        Result = null;
        Problem = null;
        AddressProblem = null;
        KeyProblem = null;
    }

    [RelayCommand]
    public void ChangeAddress() => Named.RevealAddress();

    [RelayCommand]
    public Task RetryProviders() => Named.Load();

    [RelayCommand]
    public async Task Test()
    {
        Problem = null;
        Result = null;
        if (!FormIsFilled())
        {
            return;
        }

        Busy = true;
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

    // The first test saves the Connection. A later one keeps it and only replaces a changed key,
    // unless the provider or the address changed, which makes it a different Connection: then the
    // space is asked first, because a visit to this step may have made one for it already, and a
    // second row for the same provider and address would sit on the Models screen as another one.
    private async Task<ModelConnectionSummary> SaveConnection()
    {
        var address = Named.TakesAddress ? Named.BlankAddress ?? BaseUrl.Trim() : null;
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

    // Continue with no passing test is allowed: the connection is saved as it is, not tested, and the step moves on.
    [RelayCommand]
    public async Task Continue()
    {
        if (!Passed && !await SaveUntested())
        {
            return;
        }

        navigator.NavigateTo(Addresses.SetupBot);
    }

    private async Task<bool> SaveUntested()
    {
        Problem = null;
        if (!FormIsFilled())
        {
            return false;
        }

        Busy = true;
        try
        {
            Connection = await SaveConnection();
            return true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ConnectModelViewModel));
            Problem = Failures.Sentence(exception, "Check that the server is running.");
            return false;
        }
        finally
        {
            Busy = false;
        }
    }

    private bool FormIsFilled()
    {
        (AddressProblem, KeyProblem) = Named.Missing(BaseUrl, ApiKey);
        return AddressProblem is null && KeyProblem is null && Kind.Length > 0;
    }

    #endregion
}
