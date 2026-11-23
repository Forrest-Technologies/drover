namespace Drover.Native.Platform;

/// <summary>
/// Choosing the server the app talks to. Only an HTTPS address without a path is taken, and only if what answers
/// there is a Drover server.
/// </summary>
public sealed partial class ConnectViewModel(
    ServerAddress server, AppRequests requests, Navigator navigator, ILogger<ConnectViewModel> logger) : ObservableObject
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    [ObservableProperty]
    private string address = string.Empty;

    [ObservableProperty]
    private string? problem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubmitLabel))]
    private bool busy;

    #endregion

    #region Properties

    public static string Title => "Connect to Drover";

    public string SubmitLabel => Busy ? "Connecting" : "Connect";

    #endregion

    #region Public Methods

    [RelayCommand]
    public async Task Connect()
    {
        if (ServerAddress.Parse(Address) is not { } origin)
        {
            Problem = "Enter an HTTPS address without a path.";
            return;
        }

        Busy = true;
        Problem = null;
        using var timeout = new CancellationTokenSource(patience);
        try
        {
            if (await requests.IsDroverServer(origin, logger, timeout.Token))
            {
                server.Set(origin);

                // The address the app was opened at is shown now that there is a server to ask.
                navigator.NavigateTo(navigator.Current, replace: true);
            }
            else
            {
                Problem = "That address did not respond as a Drover server.";
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not connect to the Drover server");
            Problem = "Could not reach Drover. Check the address and try again.";
        }
        finally
        {
            Busy = false;
        }
    }

    #endregion
}
