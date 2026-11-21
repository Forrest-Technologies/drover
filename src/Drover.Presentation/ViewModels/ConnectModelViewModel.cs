namespace Drover.Presentation.ViewModels;

/// <summary>The ConnectModel setup step, with the same server calls and transitions as the existing client.</summary>
public sealed partial class ConnectModelViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigator,
    ILogger<ConnectModelViewModel> logger) : ObservableObject
{
    #region Private Fields

    private const string VeniceKind = "venice";

    public const string CompatibleKind = "openai_compatible";

    public static readonly SegmentOption<string>[] Providers =
    [
        new(VeniceKind, "Venice"),
        new(CompatibleKind, "Compatible endpoint"),
    ];

    [ObservableProperty]
    private string kind = VeniceKind;

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

    #endregion

    #region Properties

    public bool Passed => Problem is null && Result is { Succeeded: true };

    public string? Outcome => Problem ?? Result?.Message;

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Public Methods

    public Task Open() => session.Load();

    public void ChooseProvider(string chosen)
    {
        Kind = chosen;
        Result = null;
        Problem = null;
    }

    [RelayCommand]
    public async Task Test()
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

    // The first test saves the Connection. A later one keeps it and only replaces a changed key,
    // unless the provider or the address changed, which makes it a different Connection: then the
    // space is asked first, because a visit to this step may have made one for it already, and a
    // second row for the same provider and address would sit on the Models screen as another one.
    private async Task<ModelConnectionSummary> SaveConnection()
    {
        var address = Kind == CompatibleKind ? BaseUrl.Trim() : null;
        var key = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim();

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
                var made = await api.CreateModelConnection(SpaceId, new(Kind, NameFor(address), address, key, null));
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

    private static string NameFor(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed) ? parsed.Host : "Venice";

    [RelayCommand]
    public void Continue() => navigator.NavigateTo(Addresses.SetupBot);

    #endregion
}
