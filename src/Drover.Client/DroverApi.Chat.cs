namespace Drover.Client;

/// <summary>Bots, threads, messages, and runs: what the chat screens call.</summary>
public sealed partial class DroverApi
{
    #region Bots

    public Task<List<BotSummary>> ListBots(Guid spaceId, CancellationToken cancellationToken = default) =>
        Get<List<BotSummary>>($"{Space(spaceId)}/bots", cancellationToken);

    public Task<BotSummary> GetBot(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<BotSummary>($"{Space(spaceId)}/bots/{botId}", cancellationToken);

    public Task<BotSummary> CreateBot(Guid spaceId, CreateBotRequest request, CancellationToken cancellationToken = default) =>
        Post<BotSummary>($"{Space(spaceId)}/bots", request, cancellationToken);

    #endregion

    #region Threads And Messages

    /// <summary>The caller's threads with one bot, newest activity first.</summary>
    public Task<List<ThreadSummary>> ListThreads(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<List<ThreadSummary>>($"{Space(spaceId)}/bots/{botId}/threads", cancellationToken);

    public Task<ThreadSummary> CreateThread(Guid spaceId, Guid botId, string? title = null, CancellationToken cancellationToken = default) =>
        Post<ThreadSummary>($"{Space(spaceId)}/bots/{botId}/threads", new CreateThreadRequest(title), cancellationToken);

    /// <summary>One thread. <see cref="ThreadSummary.ActiveRunId"/> is the run to watch, if one is still working.</summary>
    public Task<ThreadSummary> GetThread(Guid spaceId, Guid threadId, CancellationToken cancellationToken = default) =>
        Get<ThreadSummary>($"{Space(spaceId)}/threads/{threadId}", cancellationToken);

    public Task<List<MessageResponse>> ListMessages(Guid spaceId, Guid threadId, CancellationToken cancellationToken = default) =>
        Get<List<MessageResponse>>($"{Space(spaceId)}/threads/{threadId}/messages", cancellationToken);

    /// <summary>
    /// Stores the message and returns the run that answers it. It is never refused because the bot is still working:
    /// then the message comes back pending (no position) with the run under way, and <paramref name="whenBusy"/>
    /// says whether it steers that run at its next step (<see cref="BusyModes.Steer"/>, the default) or waits for it
    /// to end (<see cref="BusyModes.Queue"/>). The run's events say when it is delivered.
    /// </summary>
    public Task<SendMessageResponse> SendMessage(
        Guid spaceId,
        Guid threadId,
        string text,
        string? whenBusy = null,
        CancellationToken cancellationToken = default) =>
        Post<SendMessageResponse>($"{Space(spaceId)}/threads/{threadId}/messages", new SendMessageRequest(text, whenBusy), cancellationToken);

    #endregion

    #region Runs

    public Task<RunSummary> GetRun(Guid spaceId, Guid runId, CancellationToken cancellationToken = default) =>
        Get<RunSummary>($"{Space(spaceId)}/runs/{runId}", cancellationToken);

    public Task<RunSummary> CancelRun(Guid spaceId, Guid runId, CancellationToken cancellationToken = default) =>
        Post<RunSummary>($"{Space(spaceId)}/runs/{runId}/cancel", null, cancellationToken);

    /// <summary>
    /// Answers a tool call that waits for approval (<see cref="ToolCallView.State"/> is awaiting_approval). The
    /// reason is told to the bot when the call is denied. Throws <see cref="DroverApiException"/> with 409 when the
    /// call was already answered.
    /// </summary>
    public Task<RunSummary> DecideToolCall(
        Guid spaceId,
        Guid toolCallId,
        bool approved,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        Post<RunSummary>($"{Space(spaceId)}/tool-calls/{toolCallId}/decision", new ToolDecisionRequest(approved, reason), cancellationToken);

    /// <summary>
    /// The events of a run, from after <paramref name="afterSeq"/> to its end, each once and in order. The stream
    /// reconnects by itself with the last number it saw, so a dropped connection or a restarted server loses
    /// nothing and repeats nothing. It ends when the run does. Feed each event to <see cref="RunReducer.Apply(RunView, RunEventResponse)"/>.
    /// <paramref name="dropped"/> hears why a connection was lost, so a screen can say it is reconnecting.
    /// </summary>
    public IAsyncEnumerable<RunEventResponse> WatchRun(
        Guid spaceId,
        Guid runId,
        long afterSeq = 0,
        Action<Exception>? dropped = null,
        CancellationToken cancellationToken = default) =>
        new RunEventReader(http, $"{Space(spaceId)}/runs/{runId}", runId, dropped).Read(afterSeq, cancellationToken);

    /// <summary>
    /// Signals for the signed-in person in one space: a thread changed, a run finished, an approval is waiting.
    /// They are hints with no replay: on <see cref="UserSignalKinds.Reconnected"/>, fetch the lists again. Runs
    /// until cancelled.
    /// </summary>
    public IAsyncEnumerable<UserSignal> WatchSignals(Guid spaceId, Action<Exception>? dropped = null, CancellationToken cancellationToken = default) =>
        new SignalReader(http, $"{Space(spaceId)}/stream", dropped).Read(cancellationToken);

    #endregion
}
