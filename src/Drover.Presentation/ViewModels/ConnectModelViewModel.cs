namespace Drover.Presentation.ViewModels;

/// <summary>
/// First-run setup, step two: connect the model the bots think with, and test it. The first test saves the
/// connection; a later one keeps it and only replaces a changed key, unless the provider or the address changed,
/// which makes it a different connection.
/// </summary>
public sealed partial class ConnectModelViewModel(
    DroverApi api, SessionState session, Navigator navigator, ILogger<ConnectModelViewModel> logger) : ObservableObject
{
    #region Private Fields

    private const string VeniceKind = "venice";

    private const string CompatibleKind = "openai_compatible";

    private static readonly SegmentOption<string>[] providers =
    [
        new(VeniceKind, "Venice"),
        new(CompatibleKind, "Compatible endpoint"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCompatible), nameof(ShowVenice))]
    private string kind = VeniceKind;

    [ObservableProperty]
    private string baseUrl = string.Empty;

    [ObservableProperty]
    private string apiKey = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Passed), nameof(NotPassed), nameof(Outcome), nameof(ShownOutcome), nameof(OutcomeKind))]
    private ConnectionTestResponse? result;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Passed), nameof(NotPassed), nameof(Outcome), nameof(ShownOutcome), nameof(OutcomeKind))]
    private string? problem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownOutcome), nameof(TestLabel))]
    private bool busy;

    private ModelConnectionSummary? connection;

    private string? sentKey;

    #endregion

    #region Properties

    public static string Title => "Connect a model";

    public static string Lead => "Bots think with a model you bring. The key is stored encrypted on this server and is never shown again.";

    public static string AskingNote => "Asking the provider. This can take a few seconds.";

    public static IReadOnlyList<SegmentOption<string>> Providers => providers;

    public bool ShowCompatible => Kind == CompatibleKind;

    public bool ShowVenice => !ShowCompatible;

    public bool Passed => Problem is null && Result is { Succeeded: true };

    public bool NotPassed => !Passed;

    public string? Outcome => Problem ?? Result?.Message;

    /// <summary>What the note says: nothing while the provider is being asked.</summary>
    public string? ShownOutcome => Busy ? null : Outcome;

    public NoteKind OutcomeKind => Passed ? NoteKind.Ok : NoteKind.Danger;

    public string TestLabel => Busy ? "Testing" : "Test connection";

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Public Methods

    [RelayCommand]
    public async Task Test()
    {
        Busy = true;
        Problem = null;
        Result = null;
        try
        {
            connection = await SaveConnection();
            Result = await api.TestModelConnection(SpaceId, connection.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ConnectModelViewModel));
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    public void Continue() => navigator.NavigateTo(Addresses.SetupBot);

    #endregion

    #region Private Methods

    // Choosing another provider forgets what the last test said.
    partial void OnKindChanged(string value)
    {
        Result = null;
        Problem = null;
    }

    // The first test saves the connection. A later one keeps it and only replaces a changed key,
    // unless the provider or the address changed, which makes it a different connection.
    private async Task<ModelConnectionSummary> SaveConnection()
    {
        var address = Kind == CompatibleKind ? BaseUrl.Trim() : null;
        var key = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim();

        if (connection is null || connection.Kind != Kind || !string.Equals(connection.BaseUrl, address, StringComparison.Ordinal))
        {
            var created = await api.CreateModelConnection(SpaceId, new(Kind, NameFor(address), address, key, null));
            sentKey = key;
            return created;
        }

        if (key is not null && key != sentKey)
        {
            var updated = await api.ReplaceModelKey(SpaceId, connection.Id, new(key));
            sentKey = key;
            return updated;
        }

        return connection;
    }

    private static string NameFor(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed) ? parsed.Host : "Venice";

    #endregion
}
