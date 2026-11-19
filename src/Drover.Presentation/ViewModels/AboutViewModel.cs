namespace Drover.Presentation.ViewModels;

/// <summary>
/// What the app says about itself: its version and platform, and the one thing a person does there, saving the
/// diagnostic log to attach to a bug report. The same save is offered once after a failure that ended the app.
/// </summary>
public sealed partial class AboutViewModel(
    DiagnosticLog log,
    AppDescription app,
    TimeProvider time,
    ToastService toasts,
    ILogger<AboutViewModel> logger) : ObservableObject
{
    #region Private Fields

    public const string AfterFailure =
        "Drover stopped after a failure. Save its diagnostic log to attach to a bug report.";

    public const string Kept =
        "This device keeps a log of the app's warnings and failures. It never holds message text, names, addresses, passwords or tokens, and nothing sends it anywhere.";

    [ObservableProperty]
    private string? notice;

    [ObservableProperty]
    private bool noticeIsProblem;

    #endregion

    #region Properties

    public string Version => app.Version;

    public string Platform => app.Platform;

    #endregion

    #region Public Methods

    /// <summary>
    /// Hands the log to the device to save where the person chooses. Choosing nowhere saves nothing and says nothing.
    /// </summary>
    public async Task SaveDiagnostics(Func<string, string, Task<bool>> save)
    {
        Notice = null;
        try
        {
            if (await save(FileName(), log.Compose(app)))
            {
                (Notice, NoticeIsProblem) = ("The diagnostic log was handed to this device to save.", false);
            }
        }
        catch (DeviceCallFailedException exception)
        {
            logger.LogWarning(exception, "The diagnostic log could not be saved");
            (Notice, NoticeIsProblem) =
                ("The diagnostic log could not be saved. Try again and choose another place.", true);
        }
    }

    /// <summary>After a failure nothing handled, the next start offers the log, once.</summary>
    public void OfferAfterFailure(Func<string, string, Task<bool>> save)
    {
        if (log.TakeFailureOffer())
        {
            toasts.Show(AfterFailure, ToastKind.Warning, new("Save diagnostics", () => SaveFromToast(save)));
        }
    }

    #endregion

    #region Private Methods

    private async Task SaveFromToast(Func<string, string, Task<bool>> save)
    {
        await SaveDiagnostics(save);
        if (Notice is { } said)
        {
            toasts.Show(said, NoticeIsProblem ? ToastKind.Error : ToastKind.Success);
        }
    }

    private string FileName() => $"drover-diagnostics-{time.GetUtcNow():yyyyMMdd-HHmmss}.txt";

    #endregion
}
