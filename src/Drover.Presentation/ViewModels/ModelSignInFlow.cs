namespace Drover.Presentation.ViewModels;

/// <summary>Where a person's sign-in to a model provider stands on this device.</summary>
public enum ModelSignInStage
{
    /// <summary>Nothing started: the button that starts it.</summary>
    Ready,

    Waiting,

    Done,

    /// <summary>It ended without a sign-in; the sentence says why and the button starts it again.</summary>
    Ended,

    /// <summary>This device cannot listen, or could not now; the sentence says what to do.</summary>
    CannotListen,

    /// <summary>The server does not offer the kind; the action is to add a key instead.</summary>
    NotAvailable,
}

/// <summary>The one action a sign-in's state offers.</summary>
public enum ModelSignInAction
{
    None,
    Continue,
    Cancel,
    TryAgain,
    AddKey,
}

/// <summary>
/// One person's sign-in to a model provider with their own plan, from this device: choose the provider, approve in
/// the browser, done. The device listens on its loopback address, asks the server for the provider's page, opens it,
/// and hands the server the query string the browser brings back. The exchange, and every token, stay on the server.
/// Each state has one sentence and at most one action; the sentences the server writes (a refusal, a kind it does not
/// offer) are shown as the server wrote them.
/// </summary>
public sealed partial class ModelSignInFlow(DroverApi api, IModelSignInListener listener, ILogger logger, TimeProvider? time = null) : ObservableObject
{
    #region Private Fields

    /// <summary>How long a person has to approve: the server forgets an attempt after the same time.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(10);

    public const string WaitingWords = "Finish signing in in your browser.";

    public const string CancelledWords = "Sign-in was cancelled.";

    public const string TooLongWords = "Sign-in took too long.";

    public const string CannotListenWords =
        "Drover could not listen for the sign-in on this device. Close other apps that are signing in and try again.";

    private readonly TimeProvider clock = time ?? TimeProvider.System;

    private CancellationTokenSource? waiting;

    /// <summary>True when the wait ended because the person pressed Cancel, not because it took too long.</summary>
    private bool cancelledByPerson;

    [ObservableProperty]
    private ModelSignInStage stage = ModelSignInStage.Ready;

    [ObservableProperty]
    private string? sentence;

    [ObservableProperty]
    private ModelSignInAction action = ModelSignInAction.Continue;

    [ObservableProperty]
    private ModelConnectionSummary? connection;

    #endregion

    #region Properties

    /// <summary>The kind being signed in to, as the server describes it.</summary>
    public ModelProviderKindResponse? Kind { get; private set; }

    /// <summary>What the provider's button calls it, such as "ChatGPT".</summary>
    public string Provider => Kind?.SignInName ?? Kind?.DisplayName ?? string.Empty;

    /// <summary>The words of the screen's one primary action, which is never an icon.</summary>
    public string ContinueWords => $"Continue with {Provider}";

    /// <summary>The one line under the provider's name before anything starts.</summary>
    public string PlanLine => $"Uses your own {Provider} plan. Drover does not charge for it.";

    /// <summary>The words of <see cref="Action"/>, or null when the state has none.</summary>
    public string? ActionWords => Action switch
    {
        ModelSignInAction.Continue => ContinueWords,
        ModelSignInAction.Cancel => "Cancel",
        ModelSignInAction.TryAgain => "Try again",
        ModelSignInAction.AddKey => "Add a key",
        _ => null,
    };

    /// <summary>The provider's own page for what apps use of the plan, when it has one and it is https.</summary>
    public string? UsagePage =>
        Kind?.UsagePage is { } page && Uri.TryCreate(page, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps ? page : null;

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows a kind before anything starts: the button, or on a device that cannot listen the sentence that says so.
    /// </summary>
    public void Show(ModelProviderKindResponse kind)
    {
        Cancel(quietly: true);
        Kind = kind;
        Connection = null;

        if (listener.CanListen)
        {
            Settle(ModelSignInStage.Ready, null, ModelSignInAction.Continue);
        }
        else
        {
            Settle(
                ModelSignInStage.CannotListen,
                $"Signing in with a {Provider} plan needs the Drover desktop app for now. You can add an API key here.",
                ModelSignInAction.AddKey);
        }
    }

    /// <summary>
    /// Runs the sign-in to its end: listen, ask the server for the page, open it with <paramref name="open"/>, wait for
    /// the browser's return, and hand it to the server.
    /// </summary>
    public async Task Start(Guid spaceId, Func<string, Task> open)
    {
        if (Kind is not { } kind || Stage is ModelSignInStage.Waiting)
        {
            return;
        }

        IModelSignInReturn listening;

        try
        {
            listening = listener.Open();
        }
        catch (DeviceCallFailedException failure)
        {
            logger.LogWarning(failure, "The device could not listen for a model sign-in's return");
            Settle(ModelSignInStage.CannotListen, CannotListenWords, ModelSignInAction.TryAgain);
            return;
        }

        using (listening)
        {
            using var patience = new CancellationTokenSource(Patience, clock);
            waiting = patience;
            Settle(ModelSignInStage.Waiting, WaitingWords, ModelSignInAction.Cancel);
            await Run(spaceId, kind, listening, open, patience);
            waiting = null;
        }
    }

    /// <summary>Stops waiting for the browser. The server's attempt ends on its own.</summary>
    [RelayCommand]
    public void Cancel() => Cancel(quietly: false);

    #endregion

    #region Private Methods

    private async Task Run(Guid spaceId, ModelProviderKindResponse kind, IModelSignInReturn listening, Func<string, Task> open, CancellationTokenSource patience)
    {
        try
        {
            var started = await api.StartModelSignIn(spaceId, new(kind.Kind, listening.RedirectUri.AbsoluteUri), patience.Token);
            await open(started.Address);
            var returned = await listening.Returned.WaitAsync(patience.Token);
            var signedIn = await api.FinishModelSignIn(spaceId, started.AttemptId, new(returned), CancellationToken.None);
            Connection = signedIn;
            var who = signedIn.SignIn?.AccountLabel is { Length: > 0 } label ? $"Signed in as {label}." : "Signed in.";
            Settle(ModelSignInStage.Done, $"{who} Your bots can use your {Provider} plan.", ModelSignInAction.None);
        }
        catch (OperationCanceledException stopped) when (patience.IsCancellationRequested)
        {
            logger.LogInformation("A model sign-in stopped waiting: {Failure}", stopped.GetType().Name);
            Settle(ModelSignInStage.Ended, cancelledByPerson ? CancelledWords : TooLongWords, ModelSignInAction.Continue);
        }
        catch (DroverApiException refused) when (refused.StatusCode is HttpStatusCode.Forbidden)
        {
            logger.LogInformation("The server does not offer this sign-in: {Status}", refused.StatusCode);
            Settle(ModelSignInStage.NotAvailable, refused.Message, ModelSignInAction.AddKey);
        }
        catch (DroverApiException refused) when (refused.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            logger.LogInformation("A model sign-in ended without a sign-in: {Status}", refused.StatusCode);
            Settle(ModelSignInStage.Ended, refused.Message, ModelSignInAction.Continue);
        }
        catch (Exception failure) when (failure is DeviceCallFailedException || Failures.IsReportable(failure))
        {
            logger.LogWarning(failure, "A model sign-in could not finish");
            Settle(ModelSignInStage.Ended, Failures.Sentence(failure, "Try again."), ModelSignInAction.Continue);
        }
        finally
        {
            cancelledByPerson = false;
        }
    }

    private void Cancel(bool quietly)
    {
        if (waiting is { } current)
        {
            cancelledByPerson = true;
            current.Cancel();
        }
        else if (!quietly && Stage is ModelSignInStage.Waiting)
        {
            Settle(ModelSignInStage.Ended, CancelledWords, ModelSignInAction.Continue);
        }
    }

    private void Settle(ModelSignInStage next, string? words, ModelSignInAction offered)
    {
        Stage = next;
        Sentence = words;
        Action = offered;
        OnPropertyChanged(nameof(ActionWords));
    }

    #endregion
}
