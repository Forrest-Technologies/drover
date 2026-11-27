namespace Drover.Presentation.ViewModels;

/// <summary>Where a person's sign-in to a model provider stands on this device.</summary>
public enum ModelSignInStage
{
    /// <summary>Nothing started: the button that starts it.</summary>
    Ready,

    /// <summary>The provider's page is open and the answer comes back by itself.</summary>
    Waiting,

    /// <summary>The person enters the shown code on the provider's page; the server finishes once they approve.</summary>
    Code,

    /// <summary>The code ran out before the person approved; the button asks for a new one.</summary>
    CodeRanOut,

    /// <summary>The provider's page shows a code that the person pastes here.</summary>
    Paste,

    Done,

    /// <summary>It ended without a sign-in; the sentence says why and the button starts it again.</summary>
    Ended,

    /// <summary>This device could not listen now; the sentence says what to do.</summary>
    CannotListen,

    /// <summary>The server does not offer the kind; the action is to add a key instead.</summary>
    NotAvailable,
}

/// <summary>The one main action a sign-in's state offers.</summary>
public enum ModelSignInAction
{
    None,
    Continue,
    Cancel,
    TryAgain,
    AddKey,
    GetNewCode,
}

/// <summary>
/// One person's sign-in to a model provider with their own plan, from this device: choose the provider, approve on the
/// provider's page, done. A device that can listen on its loopback address receives the browser's return and hands
/// its query string to the server. A device that cannot (a phone, a browser tab) signs in by code: it shows the code to
/// enter on the provider's page and reads the server's progress until the server has finished, or it takes the code
/// the provider's page shows and hands it to the server. The server says which; the person never chooses. The
/// exchange, and every token, stay on the server. Each state has one sentence and one main action; the sentences the
/// server writes (a refusal, a kind it does not offer) are shown as the server wrote them.
/// </summary>
public sealed partial class ModelSignInFlow(DroverApi api, IModelSignInListener listener, ILogger logger, TimeProvider? time = null) : ObservableObject
{
    #region Private Fields

    /// <summary>How long a person has to approve: the server forgets an attempt after the same time.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(10);

    /// <summary>The longest a code may live; the server says when it ran out, and this is only a backstop.</summary>
    public static readonly TimeSpan CodePatience = TimeSpan.FromMinutes(16);

    /// <summary>How often the device reads where a sign-in by code stands.</summary>
    public static readonly TimeSpan ReadEvery = TimeSpan.FromSeconds(2);

    public const string WaitingWords = "Finish signing in in your browser.";

    public const string CancelledWords = "Sign-in was cancelled.";

    public const string TooLongWords = "Sign-in took too long.";

    public const string CodeRanOutWords = "The code ran out.";

    public const string PasteWords = "Sign in in your browser, then paste the code it shows.";

    public const string PasteRefusedWords = "That code did not work. Copy it again from the browser page.";

    public const string CannotListenWords =
        "Drover could not listen for the sign-in on this device. Close other apps that are signing in and try again.";

    /// <summary>The label of the field a pasted code goes in.</summary>
    public const string PasteLabel = "Code";

    /// <summary>The accessible name of the button that copies the shown code.</summary>
    public const string CopyWords = "Copy code";

    public const string CancelWords = "Cancel";

    private readonly TimeProvider clock = time ?? TimeProvider.System;

    private CancellationTokenSource? waiting;

    /// <summary>The next pasted code, once the person sends one.</summary>
    private TaskCompletionSource<string>? pasted;

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

    /// <summary>The code the person enters on the provider's page, while <see cref="Stage"/> is <see cref="ModelSignInStage.Code"/>.</summary>
    [ObservableProperty]
    private string? code;

    /// <summary>The provider's page, while the person signs in there by code; https only.</summary>
    [ObservableProperty]
    private string? page;

    /// <summary>What the person pasted from the provider's page.</summary>
    [ObservableProperty]
    private string pastedCode = string.Empty;

    #endregion

    #region Properties

    /// <summary>The kind being signed in to, as the server describes it.</summary>
    public ModelProviderKindResponse? Kind { get; private set; }

    /// <summary>What the provider's button calls it, such as "ChatGPT".</summary>
    public string Provider => Kind?.SignInName ?? Kind?.DisplayName ?? string.Empty;

    /// <summary>The words of the screen's one primary action, which is never an icon.</summary>
    public string ContinueWords => $"Continue with {Provider}";

    /// <summary>What the person signs in with: their paid "plan", or for a provider whose sign-in is open to any app their "account".</summary>
    public string Noun => Kind?.PersonalPlan is false ? "account" : "plan";

    /// <summary>The one line under the provider's name before anything starts.</summary>
    public string PlanLine => $"Uses your own {Provider} {Noun}. Drover does not charge for it.";

    /// <summary>The words of <see cref="Action"/>, or null when the state has none.</summary>
    public string? ActionWords => Action switch
    {
        ModelSignInAction.Continue => Stage is ModelSignInStage.Paste ? "Continue" : ContinueWords,
        ModelSignInAction.Cancel => CancelWords,
        ModelSignInAction.TryAgain => "Try again",
        ModelSignInAction.AddKey => "Add a key",
        ModelSignInAction.GetNewCode => "Get a new code",
        _ => null,
    };

    /// <summary>True while the field for a pasted code is shown.</summary>
    public bool ShowsPaste => Stage is ModelSignInStage.Paste;

    /// <summary>True when the state offers Cancel beside its main action, as the paste step does.</summary>
    public bool OffersCancel => Stage is ModelSignInStage.Paste;

    /// <summary>The provider's page as it is shown: without its scheme, query or fragment.</summary>
    public string? PageWords =>
        Page is { } address && Uri.TryCreate(address, UriKind.Absolute, out var parsed) ? $"{parsed.Host}{parsed.AbsolutePath.TrimEnd('/')}" : null;

    /// <summary>The provider's own page for what apps use of the plan, when it has one and it is https.</summary>
    public string? UsagePage =>
        Kind?.UsagePage is { } page && Uri.TryCreate(page, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps ? page : null;

    #endregion

    #region Public Methods

    /// <summary>Shows a kind before anything starts: one line and the button.</summary>
    public void Show(ModelProviderKindResponse kind)
    {
        Cancel(quietly: true);
        waiting = null;
        cancelledByPerson = false;
        pasted = null;
        Kind = kind;
        Connection = null;
        Settle(ModelSignInStage.Ready, null, ModelSignInAction.Continue);
    }

    /// <summary>
    /// Runs the sign-in to its end: listen where this device can, ask the server for the page, open it with
    /// <paramref name="open"/>, and wait for the answer the way the server says. While a pasted code is asked for,
    /// this sends the one in <see cref="PastedCode"/> instead.
    /// </summary>
    public async Task Start(Guid spaceId, Func<string, Task> open)
    {
        if (Stage is ModelSignInStage.Paste)
        {
            SendPaste();
            return;
        }

        if (Kind is not { } kind || Stage is ModelSignInStage.Waiting or ModelSignInStage.Code)
        {
            return;
        }

        IModelSignInReturn? listening = null;

        try
        {
            listening = listener.CanListen ? listener.Open(ReturnOf(kind)) : null;
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

            if (waiting == patience)
            {
                waiting = null;
            }
        }
    }

    /// <summary>Stops waiting and asks the server to cancel a sign-in by code.</summary>
    [RelayCommand]
    public void Cancel() => Cancel(quietly: false);

    #endregion

    #region Running

    private async Task Run(Guid spaceId, ModelProviderKindResponse kind, IModelSignInReturn? listening, Func<string, Task> open, CancellationTokenSource patience)
    {
        Guid? pollAttempt = null;

        try
        {
            var started = await api.StartModelSignIn(spaceId, new(kind.Kind, listening?.RedirectUri.AbsoluteUri), patience.Token);

            switch (started.Wait)
            {
                case ModelSignInWaits.Poll:
                    pollAttempt = started.AttemptId;
                    patience.Token.ThrowIfCancellationRequested();
                    patience.CancelAfter(CodePatience);
                    ShowCode(started);
                    await OpenPage(open, started.Address);
                    await Follow(spaceId, started.AttemptId, patience.Token);
                    break;
                case ModelSignInWaits.Paste:
                    Page = Https(started.Address);
                    Settle(ModelSignInStage.Paste, PasteWords, ModelSignInAction.Continue);
                    await OpenPage(open, started.Address);
                    await TakePastes(spaceId, started.AttemptId, patience.Token);
                    break;
                default:
                    Page = Https(started.Address);
                    OnPropertyChanged(nameof(PageWords));
                    await OpenPage(open, started.Address);
                    await Return(spaceId, started.AttemptId, listening, patience.Token);
                    break;
            }
        }
        catch (OperationCanceledException stopped) when (patience.IsCancellationRequested)
        {
            logger.LogInformation("A model sign-in stopped waiting: {Failure}", stopped.GetType().Name);

            if (cancelledByPerson || waiting != patience)
            {
                var words = pollAttempt is { } attemptId ? await CancelPoll(spaceId, attemptId) : CancelledWords;

                if (waiting == patience)
                {
                    Settle(ModelSignInStage.Ended, words, ModelSignInAction.Continue);
                }
            }
            else if (Stage is ModelSignInStage.Code)
            {
                Settle(ModelSignInStage.CodeRanOut, CodeRanOutWords, ModelSignInAction.GetNewCode);
            }
            else
            {
                Settle(ModelSignInStage.Ended, TooLongWords, ModelSignInAction.Continue);
            }
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
            if (waiting == patience)
            {
                cancelledByPerson = false;
                pasted = null;
            }
        }
    }

    /// <summary>
    /// The browser's return: received by this device's listener and handed to the server, or, where the device has
    /// none and the server took the return itself, read from the server's progress.
    /// </summary>
    private async Task Return(Guid spaceId, Guid attemptId, IModelSignInReturn? listening, CancellationToken cancellation)
    {
        if (listening is null)
        {
            await Follow(spaceId, attemptId, cancellation);
            return;
        }

        var returned = await listening.Returned.WaitAsync(cancellation);
        SignedIn(await api.FinishModelSignIn(spaceId, attemptId, new(returned), CancellationToken.None));
    }

    /// <summary>Reads where a sign-in the server finishes by itself stands, every couple of seconds, until it is over.</summary>
    private async Task Follow(Guid spaceId, Guid attemptId, CancellationToken cancellation)
    {
        while (true)
        {
            await Task.Delay(ReadEvery, clock, cancellation);
            var progress = await api.ReadModelSignIn(spaceId, attemptId, cancellation);

            switch (progress.Status)
            {
                case ModelSignInStatuses.Done:
                    SignedIn(progress.Connection);
                    return;
                case ModelSignInStatuses.Expired when Stage is ModelSignInStage.Code:
                    Settle(ModelSignInStage.CodeRanOut, CodeRanOutWords, ModelSignInAction.GetNewCode);
                    return;
                case ModelSignInStatuses.Expired:
                    Settle(ModelSignInStage.Ended, TooLongWords, ModelSignInAction.Continue);
                    return;
                case ModelSignInStatuses.Refused:
                    Settle(ModelSignInStage.Ended, progress.Problem ?? CancelledWords, ModelSignInAction.Continue);
                    return;
            }
        }
    }

    /// <summary>
    /// Takes pasted codes until one works. One that does not is said, and the person can paste again from the same
    /// page while the server's attempt still waits; once it does not, the sign-in has ended and starts again.
    /// </summary>
    private async Task TakePastes(Guid spaceId, Guid attemptId, CancellationToken cancellation)
    {
        while (true)
        {
            pasted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var text = await pasted.Task.WaitAsync(cancellation);

            try
            {
                SignedIn(await api.FinishModelSignIn(spaceId, attemptId, new(text), CancellationToken.None));
                return;
            }
            catch (DroverApiException refused) when (refused.StatusCode is HttpStatusCode.BadRequest)
            {
                logger.LogInformation("A pasted sign-in code was refused: {Status}", refused.StatusCode);
                var progress = await api.ReadModelSignIn(spaceId, attemptId, cancellation);

                if (progress.Status != ModelSignInStatuses.Waiting)
                {
                    Settle(ModelSignInStage.Ended, progress.Problem ?? refused.Message, ModelSignInAction.Continue);
                    return;
                }

                Settle(ModelSignInStage.Paste, PasteRefusedWords, ModelSignInAction.Continue);
            }
        }
    }

    /// <summary>Sends what the person pasted, once, when the paste step is waiting for it. An empty field sends nothing.</summary>
    private void SendPaste()
    {
        if (PastedCode.Trim() is { Length: > 0 } text && pasted is { } waitingFor)
        {
            PastedCode = string.Empty;
            waitingFor.TrySetResult(text);
        }
    }

    private void ShowCode(ModelSignInStarted started)
    {
        Code = started.Code;
        Page = Https(started.Address);
        Settle(ModelSignInStage.Code, $"Enter this code at {PageWords ?? started.Address}:", ModelSignInAction.Cancel);
    }

    /// <summary>
    /// Opens the provider's page. A device that will not open it (a browser that blocks a window it did not see the
    /// person ask for) still has the page's name on the screen, so the sign-in goes on.
    /// </summary>
    private async Task OpenPage(Func<string, Task> open, string address)
    {
        try
        {
            await open(address);
        }
        catch (Exception failure) when (failure is DeviceCallFailedException || Failures.IsReportable(failure))
        {
            logger.LogInformation("The provider's sign-in page did not open by itself: {Failure}", failure.GetType().Name);
        }
    }

    private void SignedIn(ModelConnectionSummary? signedIn)
    {
        Connection = signedIn;
        var who = signedIn?.SignIn?.AccountLabel is { Length: > 0 } label ? $"Signed in as {label}." : "Signed in.";
        Settle(ModelSignInStage.Done, $"{who} Your bots can use your {Provider} {Noun}.", ModelSignInAction.None);
    }

    #endregion

    #region Private Methods

    private async Task<string> CancelPoll(Guid spaceId, Guid attemptId)
    {
        try
        {
            await api.CancelModelSignIn(spaceId, attemptId);
            return CancelledWords;
        }
        catch (Exception failure) when (Failures.IsReportable(failure))
        {
            logger.LogWarning("The server could not cancel a model sign-in: {Failure}", failure.GetType().Name);
            return Failures.Sentence(failure, "Check Models for the result.");
        }
    }

    private void Cancel(bool quietly)
    {
        if (waiting is { } current)
        {
            cancelledByPerson = true;
            current.Cancel();
        }
        else if (!quietly && Stage is ModelSignInStage.Waiting or ModelSignInStage.Code or ModelSignInStage.Paste)
        {
            Settle(ModelSignInStage.Ended, CancelledWords, ModelSignInAction.Continue);
        }
    }

    private void Settle(ModelSignInStage next, string? words, ModelSignInAction offered)
    {
        if (next is not ModelSignInStage.Code)
        {
            Code = null;
        }

        if (next is not (ModelSignInStage.Code or ModelSignInStage.Paste))
        {
            Page = null;
            PastedCode = string.Empty;
        }

        Stage = next;
        Sentence = words;
        Action = offered;
        OnPropertyChanged(nameof(ActionWords));
        OnPropertyChanged(nameof(ShowsPaste));
        OnPropertyChanged(nameof(OffersCancel));
        OnPropertyChanged(nameof(PageWords));
    }

    /// <summary>Where the kind's provider sends the browser back on this computer, as the server says, or null when it does not say.</summary>
    private static Uri? ReturnOf(ModelProviderKindResponse kind) =>
        Uri.TryCreate(kind.SignInReturn, UriKind.Absolute, out var address) ? address : null;

    /// <summary>The address when it is an absolute https one, or null: a page this device opens or names is https only.</summary>
    private static string? Https(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps ? address : null;

    #endregion
}
