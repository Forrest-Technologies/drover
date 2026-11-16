namespace Drover.Ui.Chat;

/// <summary>
/// A bot's sandbox screen beside its conversation. Its job: let a person watch what the bot does on its screen while
/// they talk to it, and take control of the mouse and keyboard when they choose (<see cref="SandboxScreen"/>). The
/// sandbox's state is read when the panel opens and every ten seconds while it is open, so the bot's work shows.
/// Power and the terminal stay on the sandbox screen, which the panel links to.
/// </summary>
public sealed partial class ScreenPanel(
    DroverApi api,
    SessionState session,
    TimeProvider time,
    ILogger<ScreenPanel> logger) : IAsyncDisposable
{
    #region Private Fields

    private static readonly TimeSpan statusEvery = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource disposal = new();

    private BotSandboxStatus? status;

    private string? problem;

    private bool notPermitted;

    private Task? watching;

    #endregion

    #region Properties

    /// <summary>The bot whose screen is shown.</summary>
    [Parameter, EditorRequired]
    public BotSummary Bot { get; set; } = null!;

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        await disposal.CancelAsync();

        // Closing never fails because of how the refresh ended: the screen is going away either way.
        if (watching is not null)
        {
            try
            {
                await watching;
            }
            catch (Exception ended)
            {
                logger.LogDebug(ended, "The screen panel's refresh ended with a failure as it closed");
            }
        }

        disposal.Dispose();
    }

    #endregion

    #region Overrides

    protected override async Task OnInitializedAsync()
    {
        await Load();
        watching = Watch(disposal.Token);
    }

    #endregion

    #region Private Methods

    private async Task Load()
    {
        problem = null;

        try
        {
            status = await api.GetBotSandbox(SpaceId, Bot.Id, disposal.Token);
        }
        catch (DroverApiException refused) when (refused.StatusCode is HttpStatusCode.Forbidden)
        {
            logger.LogInformation(refused, "The screen of bot {BotId} is not open to this person", Bot.Id);
            notPermitted = true;
        }
        catch (OperationCanceledException stopped) when (disposal.IsCancellationRequested)
        {
            logger.LogDebug(stopped, "The screen panel closed while the sandbox state was being read");
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ScreenPanel));
            problem = Failures.Sentence(exception);
        }
    }

    private async Task Watch(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(statusEvery, time);

        try
        {
            while (!notPermitted && await timer.WaitForNextTickAsync(stop))
            {
                await Refresh(stop);
            }
        }
        catch (OperationCanceledException stopped) when (stop.IsCancellationRequested)
        {
            logger.LogDebug(stopped, "The screen panel stopped reading the sandbox state");
        }
    }

    // A failure to read the state again leaves the screen as it was; the picture says for itself when it cannot be read.
    // A read that works clears a first load that failed, so the screen recovers without a tap. Whatever goes wrong with
    // one read, the next one still comes: only closing the screen ends the reads.
    private async Task Refresh(CancellationToken stop)
    {
        try
        {
            status = await api.GetBotSandbox(SpaceId, Bot.Id, stop);
            problem = null;
            await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException stopped) when (stop.IsCancellationRequested)
        {
            logger.LogDebug(stopped, "The screen panel closed while the sandbox state was being read again");
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "The sandbox state could not be read again in the {Screen}", nameof(ScreenPanel));
        }
        catch (Exception unexpected)
        {
            logger.LogWarning(unexpected, "Reading the sandbox state again failed unexpectedly in the {Screen}; the next read still comes", nameof(ScreenPanel));
        }
    }

    #endregion
}
