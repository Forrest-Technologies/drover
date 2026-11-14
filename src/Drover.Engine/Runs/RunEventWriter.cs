namespace Drover.Engine.Runs;

/// <summary>
/// Writes one run tree's events, one writer at a time, and publishes them after they commit. Model output arrives
/// token by token and a tool's output piece by piece, so both are coalesced: a chunk is written when it reaches the
/// size limit or the flush interval passes, whichever comes first, and always before any other event, so the log
/// keeps the order things happened in.
/// What is published is exactly what was stored: live and replay are the same bytes.
/// </summary>
public sealed class RunEventWriter(
    RunTenant tenant,
    Guid rootRunId,
    ChatThread thread,
    RunEventHub hub,
    RunOptions options,
    TimeProvider time,
    ILogger logger,
    RunFence? fence = null) : IAsyncDisposable
{
    #region Private Fields

    private readonly SemaphoreSlim gate = new(1, 1);

    private readonly CancellationTokenSource stopping = new();

    private ChunkBuffer? buffer;

    private Task flushing = Task.CompletedTask;

    private Guid lastMessageId;

    private string? lastKind;

    private int partIndex;

    #endregion

    #region Properties

    /// <summary>Called when a write finds the run held by another claim, before the write throws.</summary>
    public Action? LeaseLost { get; set; }

    #endregion

    #region Public Methods

    public static NewRunEvent Event(Guid runId, string type, object payload) =>
        new(runId, type, JsonElement.Parse(RunEventTypes.Write(payload)));

    /// <summary>Adds text to the part being streamed. A change of message or of kind starts a new part.</summary>
    public Task Delta(Guid runId, Guid messageId, string kind, string text) =>
        Buffer($"delta {messageId} {kind}", () => StartDelta(runId, messageId, kind), text);

    /// <summary>Adds output to the tool call's stream. A change of call or of stream starts a new event.</summary>
    public Task Output(Guid runId, Guid toolCallId, string stream, string text) =>
        Buffer(
            $"output {toolCallId} {stream}",
            () => new ChunkBuffer(chunk => Event(runId, RunEventTypes.ToolOutput, new ToolOutput(toolCallId, stream, chunk))),
            text);

    public Task Append(Guid runId, string type, object payload) => Write(null, [Event(runId, type, payload)]);

    /// <summary>
    /// Writes the buffered delta and then the events, in one transaction. <paramref name="alongside"/> runs in that
    /// transaction first, so a status change and the event that reports it commit together or not at all.
    /// <paramref name="afterwards"/> runs in it last, once the events are in: what it writes is not checked against
    /// this run's claim, which the events before it were.
    /// </summary>
    public async Task Write(
        Func<IServiceProvider, Task>? alongside,
        IReadOnlyList<NewRunEvent> events,
        Func<IServiceProvider, Task>? afterwards = null)
    {
        await gate.WaitAsync();

        try
        {
            await WriteLocked(alongside, events, afterwards);
        }
        finally
        {
            gate.Release();
        }
    }

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        await flushing;
        stopping.Dispose();
        gate.Dispose();
    }

    #endregion

    #region Private Methods

    private async Task Buffer(string key, Func<ChunkBuffer> start, string text)
    {
        await gate.WaitAsync();

        try
        {
            if (buffer is { } current && current.Key != key)
            {
                await WriteLocked(null, []);
            }

            if (buffer is null)
            {
                buffer = start();
                buffer.Key = key;
                flushing = FlushLater(buffer);
            }

            buffer.Text.Append(text);

            if (buffer.Text.Length >= options.DeltaFlushCharacters)
            {
                await WriteLocked(null, []);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private ChunkBuffer StartDelta(Guid runId, Guid messageId, string kind)
    {
        if (messageId != lastMessageId)
        {
            partIndex = 0;
        }
        else if (kind != lastKind)
        {
            partIndex++;
        }

        lastMessageId = messageId;
        lastKind = kind;
        var index = partIndex;
        return new ChunkBuffer(chunk => Event(runId, RunEventTypes.MessageDelta, new MessageDelta(messageId, index, kind, chunk)));
    }

    private async Task WriteLocked(
        Func<IServiceProvider, Task>? alongside,
        IReadOnlyList<NewRunEvent> events,
        Func<IServiceProvider, Task>? afterwards = null)
    {
        List<NewRunEvent> writing = [];

        if (buffer is { Text.Length: > 0 } pending)
        {
            writing.Add(pending.ToEvent());
        }

        writing.AddRange(events);

        if (writing.Count == 0 && alongside is null)
        {
            return;
        }

        var stored = await Store(alongside, writing, afterwards);

        buffer = null;
        hub.Publish(rootRunId, stored);
    }

    private async Task<IReadOnlyList<RunEvent>> Store(
        Func<IServiceProvider, Task>? alongside,
        List<NewRunEvent> writing,
        Func<IServiceProvider, Task>? afterwards)
    {
        try
        {
            return await tenant.Transact(
                async services =>
                {
                    if (alongside is not null)
                    {
                        await alongside(services);
                    }

                    var appended = await services.GetRequiredService<RunEventRepository>()
                        .Append(rootRunId, thread, writing, CancellationToken.None, fence);

                    if (afterwards is not null)
                    {
                        await afterwards(services);
                    }

                    return appended;
                },
                CancellationToken.None);
        }
        catch (LeaseLostException)
        {
            LeaseLost?.Invoke();
            throw;
        }
    }

    /// <summary>Writes the chunk when the flush interval passes, unless something wrote it first.</summary>
    private async Task FlushLater(ChunkBuffer started)
    {
        try
        {
            await Task.Delay(options.DeltaFlushInterval, time, stopping.Token);
            await gate.WaitAsync(stopping.Token);

            try
            {
                if (ReferenceEquals(buffer, started))
                {
                    await WriteLocked(null, []);
                }
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogTrace("The event writer of run {RunId} stopped before a timed flush", rootRunId);
        }
        catch (LeaseLostException)
        {
            logger.LogDebug("A timed flush of run {RunId} found the run taken over", rootRunId);
        }
        catch (Exception exception) when (exception is not ObjectDisposedException)
        {
            // The chunk stays buffered, so the next write carries it and reports the failure to the run.
            logger.LogWarning(exception, "A timed flush of run {RunId} failed; the next write retries it", rootRunId);
        }
        catch (ObjectDisposedException)
        {
            logger.LogTrace("The event writer of run {RunId} outlived its host", rootRunId);
        }
    }

    #endregion

    #region Nested Types

    /// <summary>Streamed text not written yet: a message part's deltas, or a tool call's output on one stream.</summary>
    private sealed class ChunkBuffer(Func<string, NewRunEvent> toEvent)
    {
        #region Properties

        /// <summary>What the text belongs to. Text for anything else writes this first.</summary>
        public string Key { get; set; } = string.Empty;

        public StringBuilder Text { get; } = new();

        #endregion

        #region Public Methods

        public NewRunEvent ToEvent() => toEvent(Text.ToString());

        #endregion
    }

    #endregion
}
